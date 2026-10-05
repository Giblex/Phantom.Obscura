using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace PhantomVault.Core.Services.BootRom
{
    /// <summary>
    /// Builds Boot ROM bytecode. Labels are patched on <see cref="Build"/>, so forward jumps
    /// read naturally. Used by provisioning to produce the attestation ROM, and by the tests
    /// and fuzz harness to produce programs on demand.
    /// </summary>
    public sealed class RomProgramBuilder
    {
        private readonly List<byte> _code = new();
        private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
        private readonly List<(int Offset, string Label)> _patches = new();

        public const byte OpNop = 0x00;
        public const byte OpPushI64 = 0x01;
        public const byte OpDrop = 0x03;
        public const byte OpDup = 0x04;
        public const byte OpSwap = 0x05;
        public const byte OpAdd = 0x10;
        public const byte OpSub = 0x11;
        public const byte OpMul = 0x12;
        public const byte OpDiv = 0x13;
        public const byte OpMod = 0x14;
        public const byte OpAnd = 0x15;
        public const byte OpOr = 0x16;
        public const byte OpXor = 0x17;
        public const byte OpShl = 0x18;
        public const byte OpShr = 0x19;
        public const byte OpNot = 0x1A;
        public const byte OpEq = 0x20;
        public const byte OpNe = 0x21;
        public const byte OpLt = 0x22;
        public const byte OpGt = 0x23;
        public const byte OpJmp = 0x30;
        public const byte OpJz = 0x31;
        public const byte OpJnz = 0x32;
        public const byte OpLoad8 = 0x40;
        public const byte OpStore8 = 0x41;
        public const byte OpLoad64 = 0x42;
        public const byte OpStore64 = 0x43;
        public const byte OpSyscall = 0x50;
        public const byte OpHalt = 0xFF;

        public const ushort SysReadChallenge = 1;
        public const ushort SysReadIntegrity = 2;
        public const ushort SysReadBinding = 3;
        public const ushort SysEmitContribution = 4;
        public const ushort SysEmitVerdict = 5;
        public const ushort SysSha256 = 6;

        public int Position => _code.Count;

        public RomProgramBuilder Op(byte opcode)
        {
            _code.Add(opcode);
            return this;
        }

        public RomProgramBuilder Push(long value)
        {
            _code.Add(OpPushI64);
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
            _code.AddRange(buffer.ToArray());
            return this;
        }

        public RomProgramBuilder Syscall(ushort id)
        {
            _code.Add(OpSyscall);
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, id);
            _code.AddRange(buffer.ToArray());
            return this;
        }

        /// <summary>Marks the current position so jumps can target it.</summary>
        public RomProgramBuilder Label(string name)
        {
            _labels[name] = _code.Count;
            return this;
        }

        public RomProgramBuilder Jump(byte jumpOpcode, string label)
        {
            if (jumpOpcode is not (OpJmp or OpJz or OpJnz))
                throw new ArgumentException("Not a jump opcode.", nameof(jumpOpcode));

            _code.Add(jumpOpcode);
            _patches.Add((_code.Count, label));
            _code.AddRange(new byte[4]);
            return this;
        }

        /// <summary>Writes a literal block into memory, byte by byte, at <paramref name="address"/>.</summary>
        public RomProgramBuilder StoreBytes(int address, ReadOnlySpan<byte> data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                Push(address + i);
                Push(data[i]);
                Op(OpStore8);
            }
            return this;
        }

        public byte[] Build()
        {
            var image = _code.ToArray();
            foreach (var (offset, label) in _patches)
            {
                if (!_labels.TryGetValue(label, out int target))
                    throw new InvalidOperationException($"Boot ROM references undefined label '{label}'.");
                BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(offset, 4), target);
            }
            return image;
        }
    }
}
