using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using PhantomVault.Core.Services.BootRom;
using Xunit;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// The Boot ROM sandbox. The VM is a trust root, so the tests that matter most are the
    /// negative ones: a malformed or hostile image must fault, never escape, hang, or crash
    /// the process.
    /// </summary>
    public sealed class BootRomVmTests
    {
        private sealed class RecordingHost : IRomHost
        {
            public byte[] Challenge { get; init; } = new byte[32];
            public byte[] Integrity { get; init; } = new byte[32];
            public byte[] Binding { get; init; } = new byte[32];

            public List<byte[]> Contributions { get; } = new();
            public List<long> Verdicts { get; } = new();

            public void ReadChallenge(Span<byte> destination) => Challenge.CopyTo(destination);
            public void ReadIntegrity(Span<byte> destination) => Integrity.CopyTo(destination);
            public void ReadBinding(Span<byte> destination) => Binding.CopyTo(destination);
            public void EmitContribution(ReadOnlySpan<byte> contribution) => Contributions.Add(contribution.ToArray());
            public void EmitVerdict(long code) => Verdicts.Add(code);
        }

        private static RomVmResult Run(byte[] program, IRomHost host, RomVmLimits? limits = null)
            => PhantomRomVm.Execute(program, host, limits ?? RomVmLimits.Default);

        [Fact]
        public void Arithmetic_and_verdict_round_trip()
        {
            var program = new RomProgramBuilder()
                .Push(20).Push(22).Op(RomProgramBuilder.OpAdd)
                .Syscall(RomProgramBuilder.SysEmitVerdict)
                .Op(RomProgramBuilder.OpHalt)
                .Build();

            var host = new RecordingHost();
            var result = Run(program, host);

            Assert.Equal(42, result.Verdict);
            Assert.Equal(new long[] { 42 }, host.Verdicts);
            Assert.False(result.ContributionEmitted);
        }

        [Fact]
        public void A_rom_can_hash_host_inputs_and_emit_a_contribution()
        {
            var host = new RecordingHost
            {
                Binding = RandomNumberGenerator.GetBytes(32),
                Integrity = RandomNumberGenerator.GetBytes(32)
            };

            // Read binding to 0, integrity to 32, hash the 64 bytes into 64, emit it.
            var program = new RomProgramBuilder()
                .Push(0).Syscall(RomProgramBuilder.SysReadBinding)
                .Push(32).Syscall(RomProgramBuilder.SysReadIntegrity)
                .Push(0).Push(64).Push(64).Syscall(RomProgramBuilder.SysSha256)
                .Push(64).Syscall(RomProgramBuilder.SysEmitContribution)
                .Push(0).Syscall(RomProgramBuilder.SysEmitVerdict)
                .Op(RomProgramBuilder.OpHalt)
                .Build();

            var result = Run(program, host);

            var expected = SHA256.HashData([.. host.Binding, .. host.Integrity]);
            Assert.True(result.ContributionEmitted);
            Assert.Equal(expected, Assert.Single(host.Contributions));
            Assert.Equal(0, result.Verdict);
        }

        [Fact]
        public void Branching_lets_a_rom_withhold_its_contribution()
        {
            // The gate that carries the design: checks fail, so no contribution is emitted.
            var program = new RomProgramBuilder()
                .Push(0)                                   // "check failed"
                .Jump(RomProgramBuilder.OpJz, "refuse")
                .Push(0).Syscall(RomProgramBuilder.SysEmitContribution)
                .Push(0).Syscall(RomProgramBuilder.SysEmitVerdict)
                .Op(RomProgramBuilder.OpHalt)
                .Label("refuse")
                .Push(9).Syscall(RomProgramBuilder.SysEmitVerdict)
                .Op(RomProgramBuilder.OpHalt)
                .Build();

            var host = new RecordingHost();
            var result = Run(program, host);

            Assert.Equal(9, result.Verdict);
            Assert.False(result.ContributionEmitted);
            Assert.Empty(host.Contributions);
        }

        [Theory]
        [InlineData(70000)]      // past the end of the arena
        [InlineData(-1)]         // negative
        public void Memory_access_outside_the_arena_faults(long address)
        {
            var program = new RomProgramBuilder()
                .Push(address).Op(RomProgramBuilder.OpLoad8)
                .Op(RomProgramBuilder.OpHalt)
                .Build();

            Assert.Throws<RomFaultException>(() => Run(program, new RecordingHost()));
        }

        [Fact]
        public void Writing_across_the_end_of_the_arena_faults()
        {
            var limits = new RomVmLimits(MemoryBytes: 64, StackDepth: 32, InstructionBudget: 10_000);

            // A 64-bit store starting at 60 would run four bytes past the end.
            var program = new RomProgramBuilder()
                .Push(60).Push(1).Op(RomProgramBuilder.OpStore64)
                .Op(RomProgramBuilder.OpHalt)
                .Build();

            Assert.Throws<RomFaultException>(() => Run(program, new RecordingHost(), limits));
        }

        [Fact]
        public void Stack_underflow_faults()
        {
            var program = new RomProgramBuilder().Op(RomProgramBuilder.OpAdd).Op(RomProgramBuilder.OpHalt).Build();
            Assert.Throws<RomFaultException>(() => Run(program, new RecordingHost()));
        }

        [Fact]
        public void Stack_overflow_faults()
        {
            var builder = new RomProgramBuilder();
            for (int i = 0; i < 40; i++) builder.Push(i);
            var program = builder.Op(RomProgramBuilder.OpHalt).Build();

            var limits = new RomVmLimits(MemoryBytes: 1024, StackDepth: 8, InstructionBudget: 10_000);
            Assert.Throws<RomFaultException>(() => Run(program, new RecordingHost(), limits));
        }

        [Fact]
        public void An_infinite_loop_is_stopped_by_the_instruction_budget()
        {
            var program = new RomProgramBuilder()
                .Label("top")
                .Jump(RomProgramBuilder.OpJmp, "top")
                .Build();

            var limits = new RomVmLimits(MemoryBytes: 1024, StackDepth: 8, InstructionBudget: 5_000);
            var fault = Assert.Throws<RomFaultException>(() => Run(program, new RecordingHost(), limits));
            Assert.Contains("instruction budget", fault.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Halting_without_a_verdict_faults()
        {
            var program = new RomProgramBuilder().Op(RomProgramBuilder.OpHalt).Build();
            Assert.Throws<RomFaultException>(() => Run(program, new RecordingHost()));
        }

        [Fact]
        public void Unknown_opcodes_and_syscalls_fault()
        {
            Assert.Throws<RomFaultException>(() =>
                Run(new RomProgramBuilder().Op(0x7E).Build(), new RecordingHost()));

            Assert.Throws<RomFaultException>(() =>
                Run(new RomProgramBuilder().Push(0).Syscall(999).Op(RomProgramBuilder.OpHalt).Build(), new RecordingHost()));
        }

        [Fact]
        public void Division_by_zero_faults_rather_than_crashing()
        {
            var program = new RomProgramBuilder()
                .Push(1).Push(0).Op(RomProgramBuilder.OpDiv)
                .Op(RomProgramBuilder.OpHalt)
                .Build();

            Assert.Throws<RomFaultException>(() => Run(program, new RecordingHost()));
        }

        [Fact]
        public void MinValue_divided_by_minus_one_does_not_overflow()
        {
            var program = new RomProgramBuilder()
                .Push(long.MinValue).Push(-1).Op(RomProgramBuilder.OpDiv)
                .Syscall(RomProgramBuilder.SysEmitVerdict)
                .Op(RomProgramBuilder.OpHalt)
                .Build();

            var result = Run(program, new RecordingHost());
            Assert.Equal(long.MinValue, result.Verdict); // negation wraps, deliberately, without trapping
        }

        [Fact]
        public void Running_past_the_end_of_the_code_faults()
        {
            // No HALT: the program counter walks off the end.
            var program = new RomProgramBuilder().Push(1).Op(RomProgramBuilder.OpDrop).Build();
            Assert.Throws<RomFaultException>(() => Run(program, new RecordingHost()));
        }

        [Fact]
        public void Truncated_operands_fault()
        {
            Assert.Throws<RomFaultException>(() =>
                Run(new byte[] { RomProgramBuilder.OpPushI64, 1, 2, 3 }, new RecordingHost()));

            Assert.Throws<RomFaultException>(() =>
                Run(new byte[] { RomProgramBuilder.OpSyscall, 1 }, new RecordingHost()));
        }

        [Fact]
        public void Arbitrary_byte_streams_only_ever_fault_or_halt_cleanly()
        {
            // Fuzz: feed the VM random bytes as code. Whatever happens, it must stay inside the
            // sandbox's failure modes — no unhandled exception type, no hang, no escape.
            var random = new Random(20260105);
            var limits = new RomVmLimits(MemoryBytes: 4096, StackDepth: 64, InstructionBudget: 50_000);

            for (int i = 0; i < 2_000; i++)
            {
                var program = new byte[random.Next(1, 256)];
                random.NextBytes(program);

                try
                {
                    Run(program, new RecordingHost(), limits);
                }
                catch (RomFaultException)
                {
                    // The only expected failure.
                }
            }
        }
    }
}
