﻿#region License
/* 
 * Copyright (C) 1999-2016 John Källén.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2, or (at your option)
 * any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; see the file COPYING.  If not, write to
 * the Free Software Foundation, 675 Mass Ave, Cambridge, MA 02139, USA.
 */
#endregion

using Reko.Core;
using Reko.Core.Expressions;
using Reko.Core.Machine;
using Reko.Core.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Reko.Arch.Tlcs.Tlcs90
{
    public partial class Tlcs90Disassembler : DisassemblerBase<Tlcs90Instruction>
    {
        private EndianImageReader rdr;
        private Tlcs90Architecture arch;
        private MachineOperand operand;
        private PrimitiveType dataWidth;
        private int backPatchOp;

        public Tlcs90Disassembler(Tlcs90Architecture arch, EndianImageReader rdr)
        {
            this.arch = arch;
            this.rdr = rdr;
        }

        public override Tlcs90Instruction DisassembleInstruction()
        {
            byte b;
            var addr = rdr.Address;
            if (!rdr.TryReadByte(out b))
                return null;
            this.operand = null;
            this.dataWidth = null;
            this.backPatchOp = -1;

            var instr = Oprecs[b].Decode(b, this);
            if (instr == null)
                instr = new Tlcs90Instruction { Opcode = Opcode.invalid };
            var len = rdr.Address - addr;
            instr.Address = addr;
            instr.Length = (int) len;
            return instr;
        }

        private Tlcs90Instruction DecodeOperands(byte b, Opcode opcode, string format)
        {
            int op = 0;
            var ops = new MachineOperand[2];
            Constant c;
            PrimitiveType size;
            for (int i = 0; i < format.Length; ++i)
            {
                switch (format[i])
                {
                case ',':
                    continue;
                case 'I':
                    // Immediate value
                    size = GetSize(format[++i]);
                    if (!rdr.TryReadLe(size, out c))
                        return null;
                    ops[op] = new ImmediateOperand(c);
                    break;
                case 'J':
                    // Absolute jump.
                    size = GetSize(format[++i]);
                    if (!rdr.TryReadLe(size, out c))
                        return null;
                    ops[op] = AddressOperand.Ptr16(c.ToUInt16());
                    break;
                case 'H':
                    ops[op] = new RegisterOperand(Registers.hl);
                    break;
                case 'S':
                    ops[op] = new RegisterOperand(Registers.sp);
                    break;
                case 'X':
                    ops[op] = new RegisterOperand(Registers.ix);
                    break;
                case 'Y':
                    ops[op] = new RegisterOperand(Registers.iy);
                    break;
                case 'r':
                    // Register encoded in low 3 bits of b.
                    ops[op] = new RegisterOperand(Registers.byteRegs[b & 7]);
                    dataWidth = PrimitiveType.Byte;
                    break;
                case 'x':
                    this.backPatchOp = op;
                    break;
                default: throw new NotImplementedException(string.Format("Encoding '{0}' not implemented yet.", format[i]));
                }
                ++op;
            }
            return new Tlcs90Instruction
            {
                Opcode = opcode,
                op1 = op > 0 ? ops[0] : null,
                op2 = op > 1 ? ops[1] : null,
            };
        }

        private PrimitiveType GetSize(char v)
        {
            if (v == 'b')
                return PrimitiveType.Byte;
            else if (v == 'w')
                return PrimitiveType.Word16;
            throw new NotImplementedException();
        }

        private abstract class OpRecBase
        {
            public abstract Tlcs90Instruction Decode(byte b, Tlcs90Disassembler dasm);
        }

        private class OpRec : OpRecBase
        {
            private Opcode opcode;
            private string format;

            public OpRec(Opcode opcode, string format)
            {
                this.opcode = opcode;
                this.format = format;
            }

            public override Tlcs90Instruction Decode(byte b, Tlcs90Disassembler dasm)
            {
                return dasm.DecodeOperands(b, opcode, format);
            }
        }

        private class RegOpRec : OpRecBase
        {
            private RegisterStorage regByte;
            private RegisterStorage regWord;

            public RegOpRec(RegisterStorage regByte, RegisterStorage regWord)
            {
                this.regByte = regByte;
                this.regWord = regWord;
            }

            public override Tlcs90Instruction Decode(byte b, Tlcs90Disassembler dasm)
            {
                throw new NotImplementedException();
            }
        }

        private class DstOpRec : OpRecBase
        {
            private string format;

            public DstOpRec(string format)
            {
                this.format = format;
            }

            public override Tlcs90Instruction Decode(byte b, Tlcs90Disassembler dasm)
            {
                switch (format[0])
                {
                case 'M':
                    ushort absAddr;
                    if (!dasm.rdr.TryReadLeUInt16(out absAddr))
                        return null;
                    if (!dasm.rdr.TryReadByte(out b))
                        return null;
                    var instr = dstEncodings[b].Decode(b, dasm);
                    if (instr == null)
                        return null;
                    
                    var operand = MemoryOperand.Absolute(dasm.dataWidth, absAddr);
                    if (dasm.backPatchOp == 0)
                        instr.op1 = operand;
                    else if (dasm.backPatchOp == 1)
                        instr.op2 = operand;
                    else
                        return null;
                    return instr;
                }
                throw new NotImplementedException();
            }
        }

        private class SrcOpRec : OpRecBase
        {
            private string format;

            public SrcOpRec(string format)
            {
                this.format = format;
            }

            public override Tlcs90Instruction Decode(byte b, Tlcs90Disassembler dasm)
            {
                throw new NotImplementedException();
            }
        }

        private static OpRecBase[] oprecs = new OpRecBase[]
        {
            // 00
            new OpRec(Opcode.nop, ""),
            new OpRec(Opcode.halt, ""),
            new OpRec(Opcode.di, ""),
            new OpRec(Opcode.ei, ""),

            new OpRec(Opcode.invalid, ""),
            new OpRec(Opcode.invalid, ""),
            new OpRec(Opcode.invalid, ""),
            new OpRec(Opcode.incx, "Mw"),

            new OpRec(Opcode.ex, "D,H"),
            new OpRec(Opcode.ex, "A,@"),
            new OpRec(Opcode.exx, ""),
            new OpRec(Opcode.daa, "a"),

            new OpRec(Opcode.rcf, ""),
            new OpRec(Opcode.scf, ""),
            new OpRec(Opcode.ccf, ""),
            new OpRec(Opcode.decx, "M"),

            // 10
            new OpRec(Opcode.cpl, "a"),
            new OpRec(Opcode.neg, "a"),
            new OpRec(Opcode.mul, "H,n"),
            new OpRec(Opcode.div, "H,n"),

            new OpRec(Opcode.add, "X,Iw"),
            new OpRec(Opcode.add, "Y,Iw"),
            new OpRec(Opcode.add, "S,Iw"),
            new OpRec(Opcode.ldar, "HL,Iw"),

            new OpRec(Opcode.djnz, "jb"),
            new OpRec(Opcode.djnz, "B,jb"),
            new OpRec(Opcode.jp, "Jw"),
            new OpRec(Opcode.jr, "jw"),

            new OpRec(Opcode.call, "Jw"),
            new OpRec(Opcode.callr, "jw"),
            new OpRec(Opcode.ret, ""),
            new OpRec(Opcode.ret, "Ib"),    //$TODO: source doc is unclear

            // 20
            new OpRec(Opcode.ld, "a,r"),
            new OpRec(Opcode.ld, "a,r"),
            new OpRec(Opcode.ld, "a,r"),
            new OpRec(Opcode.ld, "a,r"),

            new OpRec(Opcode.ld, "a,r"),
            new OpRec(Opcode.ld, "a,r"),
            new OpRec(Opcode.ld, "a,r"),
            new OpRec(Opcode.ld, "a,Mb"),

            new OpRec(Opcode.ld, "r,a"),
            new OpRec(Opcode.ld, "r,a"),
            new OpRec(Opcode.ld, "r,a"),
            new OpRec(Opcode.ld, "r,a"),

            new OpRec(Opcode.ld, "r,a"),
            new OpRec(Opcode.ld, "r,a"),
            new OpRec(Opcode.ld, "r,a"),
            new OpRec(Opcode.ld, "Mb,a"),

            // 30
            new OpRec(Opcode.ld, "r,Ib"),
            new OpRec(Opcode.ld, "r,Ib"),
            new OpRec(Opcode.ld, "r,Ib"),
            new OpRec(Opcode.ld, "r,Ib"),

            new OpRec(Opcode.ld, "r,Ib"),
            new OpRec(Opcode.ld, "r,Ib"),
            new OpRec(Opcode.ld, "r,Ib"),
            new OpRec(Opcode.ld, "M,Ib"),

            new OpRec(Opcode.ld, "B,Iw"),
            new OpRec(Opcode.ld, "D,Iw"),
            new OpRec(Opcode.ld, "H,Iw"),
            new OpRec(Opcode.invalid, ""),

            new OpRec(Opcode.ld, "X,Iw"),
            new OpRec(Opcode.ld, "Y,Iw"),
            new OpRec(Opcode.ld, "S,Iw"),
            new OpRec(Opcode.ld, "?"),      //$TODO source doc misprint

            // 40
            new OpRec(Opcode.ld, "H,B"),
            new OpRec(Opcode.ld, "H,D"),
            new OpRec(Opcode.ld, "H,H"),    // lolwut
            new OpRec(Opcode.invalid, ""),

            new OpRec(Opcode.ld, "H,X"),
            new OpRec(Opcode.ld, "H,Y"),
            new OpRec(Opcode.ld, "H,S"),
            new OpRec(Opcode.ld, "H,Mw"),

            new OpRec(Opcode.ld, "B,H"),
            new OpRec(Opcode.ld, "D,H"),    // lolwut
            new OpRec(Opcode.ld, "H,H"),
            new OpRec(Opcode.invalid, ""),

            new OpRec(Opcode.ld, "X,H"),
            new OpRec(Opcode.ld, "Y,H"),
            new OpRec(Opcode.ld, "S,H"),
            new OpRec(Opcode.ld, "?"),

            // 50
            new OpRec(Opcode.push, "B"),
            new OpRec(Opcode.push, "D"),
            new OpRec(Opcode.push, "H"),
            new OpRec(Opcode.invalid, ""),

            new OpRec(Opcode.push, "X"),
            new OpRec(Opcode.push, "Y"),
            new OpRec(Opcode.push, "A"),
            new OpRec(Opcode.invalid, ""),

            new OpRec(Opcode.pop, "B"),
            new OpRec(Opcode.pop, "D"),
            new OpRec(Opcode.pop, "H"),
            new OpRec(Opcode.invalid, ""),

            new OpRec(Opcode.pop, "X"),
            new OpRec(Opcode.pop, "Y"),
            new OpRec(Opcode.pop, "A"),
            new OpRec(Opcode.invalid, ""),

            // 60
            new OpRec(Opcode.add, "a,Mb"),
            new OpRec(Opcode.adc, "a,Mb"),
            new OpRec(Opcode.sub, "a,Mb"),
            new OpRec(Opcode.sbc, "a,Mb"),

            new OpRec(Opcode.and, "a,Mb"),
            new OpRec(Opcode.xor, "a,Mb"),
            new OpRec(Opcode.or,  "a,Mb"),
            new OpRec(Opcode.cp,  "a,Mb"),

            new OpRec(Opcode.add, "a,Ib"),
            new OpRec(Opcode.adc, "a,Ib"),
            new OpRec(Opcode.sub, "a,Ib"),
            new OpRec(Opcode.sbc, "a,Ib"),

            new OpRec(Opcode.and, "a,Ib"),
            new OpRec(Opcode.xor, "a,Ib"),
            new OpRec(Opcode.or,  "a,Ib"),
            new OpRec(Opcode.cp,  "a,Ib"),

            // 70
            new OpRec(Opcode.add, "H,Mw"),
            new OpRec(Opcode.adc, "H,Mw"),
            new OpRec(Opcode.sub, "H,Mw"),
            new OpRec(Opcode.sbc, "H,Mw"),

            new OpRec(Opcode.and, "H,Mw"),
            new OpRec(Opcode.xor, "H,Mw"),
            new OpRec(Opcode.or,  "H,Mw"),
            new OpRec(Opcode.cp,  "H,Mw"),

            new OpRec(Opcode.add, "H,Iw"),
            new OpRec(Opcode.adc, "H,Iw"),
            new OpRec(Opcode.sub, "H,Iw"),
            new OpRec(Opcode.sbc, "H,Iw"),

            new OpRec(Opcode.and, "H,Iw"),
            new OpRec(Opcode.xor, "H,Iw"),
            new OpRec(Opcode.or,  "H,Iw"),
            new OpRec(Opcode.cp,  "H,Iw"),

            // 80
            new OpRec(Opcode.inc, "r"),
            new OpRec(Opcode.inc, "r"),
            new OpRec(Opcode.inc, "r"),
            new OpRec(Opcode.inc, "r"),

            new OpRec(Opcode.inc, "r"),
            new OpRec(Opcode.inc, "r"),
            new OpRec(Opcode.inc, "r"),
            new OpRec(Opcode.inc, "Mb"),

            new OpRec(Opcode.dec, "r"),
            new OpRec(Opcode.dec, "r"),
            new OpRec(Opcode.dec, "r"),
            new OpRec(Opcode.dec, "r"),

            new OpRec(Opcode.dec, "r"),
            new OpRec(Opcode.dec, "r"),
            new OpRec(Opcode.dec, "r"),
            new OpRec(Opcode.dec, "Mb"),

            // 90
            new OpRec(Opcode.inc, "B"),
            new OpRec(Opcode.inc, "D"),
            new OpRec(Opcode.inc, "H"),
            new OpRec(Opcode.invalid, ""),

            new OpRec(Opcode.inc, "X"),
            new OpRec(Opcode.inc, "Y"),
            new OpRec(Opcode.inc, "A"),
            new OpRec(Opcode.invalid, ""),

            new OpRec(Opcode.dec, "B"),
            new OpRec(Opcode.dec, "D"),
            new OpRec(Opcode.dec, "H"),
            new OpRec(Opcode.invalid, ""),

            new OpRec(Opcode.dec, "X"),
            new OpRec(Opcode.dec, "Y"),
            new OpRec(Opcode.dec, "A"),
            new OpRec(Opcode.invalid, ""),

            // A0
            new OpRec(Opcode.rrc, ""),
            new OpRec(Opcode.rrc, ""),
            new OpRec(Opcode.rl, ""),
            new OpRec(Opcode.rr, ""),

            new OpRec(Opcode.sla, ""),
            new OpRec(Opcode.sra, ""),
            new OpRec(Opcode.sll, ""),
            new OpRec(Opcode.srl, ""),

            new OpRec(Opcode.bit, "i,Mb"),
            new OpRec(Opcode.bit, "i,Mb"),
            new OpRec(Opcode.bit, "i,Mb"),
            new OpRec(Opcode.bit, "i,Mb"),

            new OpRec(Opcode.bit, "i,Mb"),
            new OpRec(Opcode.bit, "i,Mb"),
            new OpRec(Opcode.bit, "i,Mb"),
            new OpRec(Opcode.bit, "i,Mb"),

            // B0
            new OpRec(Opcode.res, "i,Mb"),
            new OpRec(Opcode.res, "i,Mb"),
            new OpRec(Opcode.res, "i,Mb"),
            new OpRec(Opcode.res, "i,Mb"),

            new OpRec(Opcode.res, "i,Mb"),
            new OpRec(Opcode.res, "i,Mb"),
            new OpRec(Opcode.res, "i,Mb"),
            new OpRec(Opcode.res, "i,Mb"),

            new OpRec(Opcode.set, "i,Mb"),
            new OpRec(Opcode.set, "i,Mb"),
            new OpRec(Opcode.set, "i,Mb"),
            new OpRec(Opcode.set, "i,Mb"),

            new OpRec(Opcode.set, "i,Mb"),
            new OpRec(Opcode.set, "i,Mb"),
            new OpRec(Opcode.set, "i,Mb"),
            new OpRec(Opcode.set, "i,Mb"),

            // C0
            new OpRec(Opcode.jr, "c,jb"),
            new OpRec(Opcode.jr, "c,jb"),
            new OpRec(Opcode.jr, "c,jb"),
            new OpRec(Opcode.jr, "c,jb"),

            new OpRec(Opcode.jr, "c,jb"),
            new OpRec(Opcode.jr, "c,jb"),
            new OpRec(Opcode.jr, "c,jb"),
            new OpRec(Opcode.jr, "c,jb"),

            new OpRec(Opcode.jr, "jb"),
            new OpRec(Opcode.jr, "c,jb"),
            new OpRec(Opcode.jr, "c,jb"),
            new OpRec(Opcode.jr, "c,jb"),

            new OpRec(Opcode.jr, "c,jb"),
            new OpRec(Opcode.jr, "c,jb"),
            new OpRec(Opcode.jr, "c,jb"),
            new OpRec(Opcode.jr, "c,jb"),

            // D0
            new OpRec(Opcode.invalid, ""),
            new OpRec(Opcode.invalid, ""),
            new OpRec(Opcode.invalid, ""),
            new OpRec(Opcode.invalid, ""),

            new OpRec(Opcode.invalid, ""),
            new OpRec(Opcode.invalid, ""),
            new OpRec(Opcode.invalid, ""),
            new OpRec(Opcode.invalid, ""),

            new OpRec(Opcode.invalid, ""),
            new OpRec(Opcode.invalid, ""),
            new OpRec(Opcode.invalid, ""),
            new OpRec(Opcode.invalid, ""),

            new OpRec(Opcode.invalid, ""),
            new OpRec(Opcode.invalid, ""),
            new OpRec(Opcode.invalid, ""),
            new OpRec(Opcode.invalid, ""),

            // E0
            new SrcOpRec("B"),
            new SrcOpRec("D"),
            new SrcOpRec("H"),
            new SrcOpRec("M"),

            new SrcOpRec("X"),
            new SrcOpRec("Y"),
            new SrcOpRec("S"),
            new SrcOpRec("m"),

            new DstOpRec("B"),
            new DstOpRec("D"),
            new DstOpRec("H"),
            new DstOpRec("M"),

            new DstOpRec("X"),
            new DstOpRec("Y"),
            new DstOpRec("S"),
            new DstOpRec("m"),

            // 00
            new SrcOpRec("EX"),
            new SrcOpRec("EY"),
            new SrcOpRec("ES"),
            new SrcOpRec("EH"),

            new DstOpRec("EX"),
            new DstOpRec("EY"),
            new DstOpRec("ES"),
            new DstOpRec("EH"),

            new RegOpRec(Registers.b, Registers.bc),
            new RegOpRec(Registers.c, Registers.de),
            new RegOpRec(Registers.d, Registers.hl),
            new RegOpRec(Registers.e, null),

            new RegOpRec(Registers.h, Registers.ix),
            new RegOpRec(Registers.l, Registers.iy),
            new RegOpRec(Registers.a, Registers.sp),
            new OpRec(Opcode.invalid, "?"),     //$TODO: unclear in docs
        };

        private static OpRecBase[] Oprecs
        {
            get
            {
                return oprecs;
            }

            set
            {
                oprecs = value;
            }
        }
    }
}
