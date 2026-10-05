using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PhantomVault.Core.Services.BootRom
{
    /// <summary>Raised when a ROM breaks the sandbox's rules. Always fatal to the run.</summary>
    public sealed class RomFaultException : Exception
    {
        public RomFaultException(string message) : base(message) { }
    }

    /// <summary>
    /// Everything the sandbox is allowed to touch. There is no file, network, clock or random
    /// source: every input is supplied by the host, so a run is deterministic and a ROM cannot
    /// reach the machine it executes on.
    /// </summary>
    public interface IRomHost
    {
        /// <summary>32 fresh bytes, unique per run — prevents replay of a recorded transcript.</summary>
        void ReadChallenge(Span<byte> destination);

        /// <summary>32-byte digest of the integrity watchdog's current verdict.</summary>
        void ReadIntegrity(Span<byte> destination);

        /// <summary>32-byte digest of device/USB binding facts.</summary>
        void ReadBinding(Span<byte> destination);

        /// <summary>The ROM's 32-byte contribution to vault key derivation.</summary>
        void EmitContribution(ReadOnlySpan<byte> contribution);

        /// <summary>Terminal verdict: 0 is success, anything else is the ROM refusing.</summary>
        void EmitVerdict(long code);
    }

    /// <summary>Sandbox ceilings. Exceeding any of them faults the run.</summary>
    public readonly record struct RomVmLimits(int MemoryBytes, int StackDepth, long InstructionBudget)
    {
        /// <summary>Enough for attestation logic, small enough that a bad ROM cannot stall startup.</summary>
        public static RomVmLimits Default => new(MemoryBytes: 64 * 1024, StackDepth: 256, InstructionBudget: 2_000_000);
    }

    /// <summary>Outcome of a completed run.</summary>
    public readonly record struct RomVmResult(long Verdict, bool ContributionEmitted, long InstructionsUsed);

    /// <summary>
    /// A deterministic stack machine for Boot ROM images. Memory is a fixed arena, the operand
    /// stack is depth-capped, and every instruction costs budget, so the worst a malformed or
    /// hostile ROM can do is fault — which fails closed at the caller.
    /// </summary>
    public static class PhantomRomVm
    {
        // Stack and arithmetic
        private const byte OpNop = 0x00;
        private const byte OpPushI64 = 0x01;
        private const byte OpDrop = 0x03;
        private const byte OpDup = 0x04;
        private const byte OpSwap = 0x05;
        private const byte OpAdd = 0x10;
        private const byte OpSub = 0x11;
        private const byte OpMul = 0x12;
        private const byte OpDiv = 0x13;
        private const byte OpMod = 0x14;
        private const byte OpAnd = 0x15;
        private const byte OpOr = 0x16;
        private const byte OpXor = 0x17;
        private const byte OpShl = 0x18;
        private const byte OpShr = 0x19;
        private const byte OpNot = 0x1A;

        // Comparison
        private const byte OpEq = 0x20;
        private const byte OpNe = 0x21;
        private const byte OpLt = 0x22;
        private const byte OpGt = 0x23;

        // Control flow
        private const byte OpJmp = 0x30;
        private const byte OpJz = 0x31;
        private const byte OpJnz = 0x32;

        // Memory
        private const byte OpLoad8 = 0x40;
        private const byte OpStore8 = 0x41;
        private const byte OpLoad64 = 0x42;
        private const byte OpStore64 = 0x43;

        // Host interface
        private const byte OpSyscall = 0x50;
        private const byte OpHalt = 0xFF;

        private const ushort SysReadChallenge = 1;
        private const ushort SysReadIntegrity = 2;
        private const ushort SysReadBinding = 3;
        private const ushort SysEmitContribution = 4;
        private const ushort SysEmitVerdict = 5;
        private const ushort SysSha256 = 6;

        private const int DigestSize = 32;

        /// <summary>Largest buffer a ROM may hash in one call, bounding syscall work.</summary>
        private const int MaxHashInput = 16 * 1024;

        public static RomVmResult Execute(ReadOnlySpan<byte> program, IRomHost host, RomVmLimits limits)
        {
            ArgumentNullException.ThrowIfNull(host);
            if (program.IsEmpty)
                throw new RomFaultException("Boot ROM program is empty.");
            if (limits.MemoryBytes <= 0 || limits.StackDepth <= 0 || limits.InstructionBudget <= 0)
                throw new ArgumentException("Sandbox limits must be positive.", nameof(limits));

            var memory = new byte[limits.MemoryBytes];
            var stack = new long[limits.StackDepth];
            int sp = 0;
            int pc = 0;
            long used = 0;
            long verdict = -1;
            bool verdictEmitted = false;
            bool contributionEmitted = false;

            void Push(long value)
            {
                if (sp >= stack.Length) throw new RomFaultException("Boot ROM overflowed the operand stack.");
                stack[sp++] = value;
            }

            long Pop()
            {
                if (sp <= 0) throw new RomFaultException("Boot ROM underflowed the operand stack.");
                return stack[--sp];
            }

            // Every memory touch goes through here: no path writes outside the arena.
            void CheckRange(long address, long length)
            {
                if (address < 0 || length < 0 || address + length > memory.Length)
                    throw new RomFaultException("Boot ROM accessed memory outside its sandbox.");
            }

            int ReadI32(ReadOnlySpan<byte> code, int at)
            {
                if (at + 4 > code.Length) throw new RomFaultException("Boot ROM ran past the end of its code.");
                return BinaryPrimitives.ReadInt32LittleEndian(code.Slice(at, 4));
            }

            try
            {
                while (true)
                {
                    if (++used > limits.InstructionBudget)
                        throw new RomFaultException("Boot ROM exceeded its instruction budget.");
                    if (pc < 0 || pc >= program.Length)
                        throw new RomFaultException("Boot ROM program counter left the code segment.");

                    byte op = program[pc++];
                    switch (op)
                    {
                        case OpNop:
                            break;

                        case OpPushI64:
                            if (pc + 8 > program.Length) throw new RomFaultException("Truncated PUSH operand.");
                            Push(BinaryPrimitives.ReadInt64LittleEndian(program.Slice(pc, 8)));
                            pc += 8;
                            break;

                        case OpDrop: Pop(); break;
                        case OpDup:
                        {
                            long v = Pop();
                            Push(v);
                            Push(v);
                            break;
                        }
                        case OpSwap:
                        {
                            long b = Pop(), a = Pop();
                            Push(b);
                            Push(a);
                            break;
                        }

                        case OpAdd: { long b = Pop(), a = Pop(); Push(unchecked(a + b)); break; }
                        case OpSub: { long b = Pop(), a = Pop(); Push(unchecked(a - b)); break; }
                        case OpMul: { long b = Pop(), a = Pop(); Push(unchecked(a * b)); break; }
                        case OpDiv:
                        {
                            long b = Pop(), a = Pop();
                            if (b == 0) throw new RomFaultException("Boot ROM divided by zero.");
                            // long.MinValue / -1 overflows; neither operand is attacker-controlled
                            // in a trusted ROM, but a hostile image must not crash the process.
                            Push(b == -1 ? unchecked(-a) : a / b);
                            break;
                        }
                        case OpMod:
                        {
                            long b = Pop(), a = Pop();
                            if (b == 0) throw new RomFaultException("Boot ROM divided by zero.");
                            Push(b == -1 ? 0 : a % b);
                            break;
                        }
                        case OpAnd: { long b = Pop(), a = Pop(); Push(a & b); break; }
                        case OpOr: { long b = Pop(), a = Pop(); Push(a | b); break; }
                        case OpXor: { long b = Pop(), a = Pop(); Push(a ^ b); break; }
                        case OpShl: { long b = Pop(), a = Pop(); Push(b is < 0 or > 63 ? 0 : a << (int)b); break; }
                        case OpShr: { long b = Pop(), a = Pop(); Push(b is < 0 or > 63 ? 0 : (long)((ulong)a >> (int)b)); break; }
                        case OpNot: Push(~Pop()); break;

                        case OpEq: { long b = Pop(), a = Pop(); Push(a == b ? 1 : 0); break; }
                        case OpNe: { long b = Pop(), a = Pop(); Push(a != b ? 1 : 0); break; }
                        case OpLt: { long b = Pop(), a = Pop(); Push(a < b ? 1 : 0); break; }
                        case OpGt: { long b = Pop(), a = Pop(); Push(a > b ? 1 : 0); break; }

                        case OpJmp: pc = ReadI32(program, pc); break;
                        case OpJz:
                        {
                            int target = ReadI32(program, pc);
                            pc += 4;
                            if (Pop() == 0) pc = target;
                            break;
                        }
                        case OpJnz:
                        {
                            int target = ReadI32(program, pc);
                            pc += 4;
                            if (Pop() != 0) pc = target;
                            break;
                        }

                        case OpLoad8:
                        {
                            long address = Pop();
                            CheckRange(address, 1);
                            Push(memory[address]);
                            break;
                        }
                        case OpStore8:
                        {
                            long value = Pop(), address = Pop();
                            CheckRange(address, 1);
                            memory[address] = (byte)(value & 0xFF);
                            break;
                        }
                        case OpLoad64:
                        {
                            long address = Pop();
                            CheckRange(address, 8);
                            Push(BinaryPrimitives.ReadInt64LittleEndian(memory.AsSpan((int)address, 8)));
                            break;
                        }
                        case OpStore64:
                        {
                            long value = Pop(), address = Pop();
                            CheckRange(address, 8);
                            BinaryPrimitives.WriteInt64LittleEndian(memory.AsSpan((int)address, 8), value);
                            break;
                        }

                        case OpSyscall:
                        {
                            if (pc + 2 > program.Length) throw new RomFaultException("Truncated SYSCALL operand.");
                            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(program.Slice(pc, 2));
                            pc += 2;

                            switch (id)
                            {
                                case SysReadChallenge:
                                {
                                    long destination = Pop();
                                    CheckRange(destination, DigestSize);
                                    host.ReadChallenge(memory.AsSpan((int)destination, DigestSize));
                                    break;
                                }
                                case SysReadIntegrity:
                                {
                                    long destination = Pop();
                                    CheckRange(destination, DigestSize);
                                    host.ReadIntegrity(memory.AsSpan((int)destination, DigestSize));
                                    break;
                                }
                                case SysReadBinding:
                                {
                                    long destination = Pop();
                                    CheckRange(destination, DigestSize);
                                    host.ReadBinding(memory.AsSpan((int)destination, DigestSize));
                                    break;
                                }
                                case SysEmitContribution:
                                {
                                    long source = Pop();
                                    CheckRange(source, DigestSize);
                                    host.EmitContribution(memory.AsSpan((int)source, DigestSize));
                                    contributionEmitted = true;
                                    break;
                                }
                                case SysEmitVerdict:
                                {
                                    verdict = Pop();
                                    host.EmitVerdict(verdict);
                                    verdictEmitted = true;
                                    break;
                                }
                                case SysSha256:
                                {
                                    long destination = Pop(), length = Pop(), source = Pop();
                                    if (length > MaxHashInput)
                                        throw new RomFaultException("Boot ROM hash input is too large.");
                                    CheckRange(source, length);
                                    CheckRange(destination, DigestSize);
                                    SHA256.HashData(
                                        memory.AsSpan((int)source, (int)length),
                                        memory.AsSpan((int)destination, DigestSize));
                                    break;
                                }
                                default:
                                    throw new RomFaultException($"Boot ROM called unknown syscall {id}.");
                            }
                            break;
                        }

                        case OpHalt:
                            if (!verdictEmitted)
                                throw new RomFaultException("Boot ROM halted without emitting a verdict.");
                            return new RomVmResult(verdict, contributionEmitted, used);

                        default:
                            throw new RomFaultException($"Boot ROM used an unknown opcode 0x{op:X2}.");
                    }
                }
            }
            finally
            {
                // The arena holds the challenge, the binding digest and the contribution.
                CryptographicOperations.ZeroMemory(memory);
                Array.Clear(stack);
            }
        }
    }
}
