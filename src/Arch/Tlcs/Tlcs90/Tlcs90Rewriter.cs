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

using Reko.Core.Rtl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using Reko.Core;
using System.Diagnostics;

namespace Reko.Arch.Tlcs.Tlcs90
{
    public partial class Tlcs90Rewriter : IEnumerable<RtlInstructionCluster>
    {
        private Frame frame;
        private IRewriterHost host;
        private EndianImageReader rdr;
        private ProcessorState state;
        private Tlcs90Architecture arch;
        private IEnumerator<Tlcs90Instruction> dasm;
        private Tlcs90Instruction instr;
        private RtlInstructionCluster rtlc;
        private RtlEmitter m;

        public Tlcs90Rewriter(Tlcs90Architecture arch, EndianImageReader rdr, ProcessorState state, Frame frame, IRewriterHost host)
        {
            this.arch = arch;
            this.rdr = rdr;
            this.state = state;
            this.frame = frame;
            this.host = host;
            this.dasm = new Tlcs90Disassembler(arch, rdr).GetEnumerator();
        }

        public IEnumerator<RtlInstructionCluster> GetEnumerator()
        {
            while (dasm.MoveNext())
            {
                this.instr = dasm.Current;
                this.rtlc = new RtlInstructionCluster(instr.Address, instr.Length);
                rtlc.Class = RtlClass.Linear;
                this.m = new RtlEmitter(rtlc.Instructions);
                switch (instr.Opcode)
                {
                default:
                    EmitUnitTest();
                    Invalid();
                    break;
                case Opcode.invalid:
                    Invalid();
                    break;
                case Opcode.jp: RewriteJp(); break;
                case Opcode.nop: m.Nop(); break;
                }
                yield return rtlc;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private void Invalid()
        {
            host.Error(
               instr.Address,
               string.Format(
                   "Rewriting of TLCS-90 instruction '{0}' not implemented yet.",
                   instr.Opcode));
            rtlc.Class = RtlClass.Invalid;
            m.Invalid();
        }


        [Conditional("DEBUG")]
        private void EmitUnitTest()
        {
            EmitUnitTest("Tlcs90_rw_");

        }

        [Conditional("DEBUG")]
        private void EmitUnitTest(string prefix)
        {
            //if (seen.Contains(dasm.Current.Opcode))
            //    return;
            //seen.Add(dasm.Current.Opcode);

            var r2 = rdr.Clone();
            r2.Offset -= dasm.Current.Length;
            var bytes = r2.ReadBytes(dasm.Current.Length);
            Debug.WriteLine("        [Test]");
            Debug.WriteLine("        public void {0}{1}()", prefix, dasm.Current.Opcode);
            Debug.WriteLine("        {");
            Debug.Write("            BuildTest(");
            Debug.Write(string.Join(
                ", ",
                bytes.Select(b => string.Format("0x{0:X2}", (int)b))));
            Debug.WriteLine(");\t// " + dasm.Current.ToString());
            Debug.WriteLine("            AssertCode(");
            Debug.WriteLine("                \"0|L--|{0}({1}): 1 instructions\",", dasm.Current.Address, bytes.Length);
            Debug.WriteLine("                \"1|L--|@@@\");");
            Debug.WriteLine("        }");
            Debug.WriteLine("");
        }
    }
}
