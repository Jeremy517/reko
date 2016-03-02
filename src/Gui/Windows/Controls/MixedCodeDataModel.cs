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
using Reko.Core.Machine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Diagnostics;

namespace Reko.Gui.Windows.Controls
{
    /// <summary>
    /// Provides a text model that mixes code and data.
    /// </summary>
    public partial class MixedCodeDataModel : TextViewModel
    {
        const int BytesPerLine = 16;

        private Program program;
        private Address currentPosition;
        private Dictionary<ImageMapBlock, MachineInstruction[]> instructions;

        public MixedCodeDataModel(Program program)
        {
            this.program = program;
            var firstSeg = program.ImageMap.Segments.Values.FirstOrDefault();
            if (firstSeg == null)
            {
                this.currentPosition = program.ImageMap.BaseAddress;
                this.StartPosition = program.ImageMap.BaseAddress;
                this.EndPosition = program.ImageMap.BaseAddress;
                this.LineCount = 0;
            }
            else
            {
                var lastSeg = program.ImageMap.Segments.Values.Last();
                this.currentPosition = firstSeg.Address;
                this.StartPosition = firstSeg.Address;
                this.EndPosition = lastSeg.EndAddress;
                this.CollectInstructions();
                this.LineCount = CountLines();
            }
        }

        public object CurrentPosition {  get { return currentPosition; } }
        public object StartPosition { get; private set;  }
        public object EndPosition { get; private set;  }

        public int LineCount { get; private set; }

        private int CountLines()
        {
            int sum = 0;
            foreach (var item in program.ImageMap.Items.Values)
            {
                var bi = item as ImageMapBlock;
                if (bi != null)
                {
                    sum += CountDisassembledLines(bi);
                }
                else
                {
                    sum += CountMemoryLines(item);
                }
            }
            return sum;
        }

        /// <summary>
        /// Count the number of lines a memory area subtends.
        /// </summary>
        /// <remarks>
        /// We align mempry spans on 16-byte boundaries (//$REVIEW for now,
        /// this should be user-adjustable) so if we have a memory span 
        /// straddling such a boundary, we have to account for it. E.g. the
        /// span [01FC-0201] should be rendered:
        /// <code>
        /// 01FC                                    0C 0D 0F             ....
        /// 0200 00 01                                       ..
        /// </code>
        /// and therefore requires 2 lines even though the number of bytes is
        /// less than 16.
        /// </remarks>
        /// <param name="item"></param>
        /// <returns></returns>
        private int CountMemoryLines(ImageMapItem item)
        {
            if (item.Size == 0)
                return 0;       //$TODO: this shouldn't ever happen!
            var linStart = item.Address.ToLinear();
            var linEnd = linStart + item.Size;
            linStart = Align(linStart, BytesPerLine);
            linEnd = Align(linEnd + (BytesPerLine - 1), BytesPerLine);
            return (int)(linEnd - linStart) / BytesPerLine;
        }

        private int CountDisassembledLines(ImageMapBlock bi)
        {
            return instructions[bi].Length;
        }

        private static ulong Align(ulong ul, uint alignment)
        {
            return alignment * (ul / alignment);
        }

        private static Address Align(Address addr, uint alignment)
        {
            var lin = addr.ToLinear();
            var linAl = Align(lin, alignment);
            return addr - (int)(lin - linAl);
        }

        /// <summary>
        /// Preemptively collect the machine code instructions
        /// in all image map blocks.
        /// </summary>
        private void CollectInstructions()
        {
            this.instructions = new Dictionary<ImageMapBlock, MachineInstruction[]>();
            foreach (var bi in program.ImageMap.Items.Values.OfType<ImageMapBlock>())
            {
                var instrs = new List<MachineInstruction>();
                var addrStart = bi.Address;
                var addrEnd = bi.Address + bi.Size;
                var dasm = program.CreateDisassembler(addrStart).GetEnumerator();
                while (dasm.MoveNext() && dasm.Current.Address < addrEnd)
                {
                    instrs.Add(dasm.Current);
                }
                instructions.Add(bi, instrs.ToArray());
            }
        }

        public int ComparePositions(object a, object b)
        {
            var diff = (Address)a - (Address)b;
            return diff.CompareTo(0);
        }

        public Tuple<int, int> GetPositionAsFraction()
        {
            var linPos = currentPosition;
            long numer = 0;
            long denom = 0;
            foreach (var item in program.ImageMap.Items.Values)
            {
                if (item.Address <= currentPosition)
                {
                    if (item.IsInRange(currentPosition))
                    {
                        numer += (currentPosition - item.Address);
                    }
                    else
                    {
                        numer += item.Size;
                    }
                }
                denom += item.Size;
            }
            while (denom >= 0x80000000)
            {
                numer >>= 1;
                denom >>= 1;
            }
            return Tuple.Create((int)numer, (int)denom);
        }

        public int MoveToLine(object position, int offset)
        {
            if (position == null)
                throw new ArgumentNullException("position");
            currentPosition = (Address)position;
            if (offset == 0)
                return 0;
            int moved = 0;
            if (offset > 0)
            {
                ImageMapItem item;
                if (!program.ImageMap.TryFindItem(currentPosition, out item))
                    return moved;
                int iItem = program.ImageMap.Items.IndexOfKey(item.Address);
                for (;;)
                {
                    Debug.Assert(item != null);
                    var bi = item as ImageMapBlock;
                    if (bi != null)
                    {
                        var instrs = instructions[bi];
                        int i = FindIndexOfInstructionAddress(instrs, currentPosition);
                        Debug.Assert(i >= 0, "TryFindItem said this item contains the address.");
                        int iNew = i + offset;
                        if (0 <= iNew && iNew < instrs.Length)
                        {
                            moved += offset;
                            currentPosition = instrs[iNew].Address;
                            return moved;
                        }
                        // Fell off the end.

                        if (offset > 0)
                        {
                            moved += instrs.Length - i;
                            offset -= instrs.Length - i;
                        }
                        else
                        {
                            moved -= i;
                            offset += i;
                        }
                    }
                    else
                    {
                        // Determine current line # within memory block

                        int i = FindIndexOfMemoryAddress(item, currentPosition);
                        int iEnd = FindIndexOfMemoryAddress(item, item.Address + item.Size);
                        Debug.Assert(i >= 0, "Should have been inside item");
                        int iNew = i + offset;
                        if (0 <= iNew && iNew <= iEnd)
                        {
                            moved += offset;
                            currentPosition = GetAddressOfLine(item, iNew);
                            return moved;
                        }
                        // Fall of the end
                        if (offset > 0)
                        {
                            moved += iEnd - i;
                            offset -= iEnd - i;
                        }
                        else
                        {
                            moved -= i;
                            offset += i;
                        }
                    }
                    iItem += offset > 0 ? 1 : -1;
                    if (iItem < 0)
                    {
                        currentPosition = (Address)this.StartPosition;
                        return moved;
                    }
                    else if (iItem >= program.ImageMap.Items.Count)
                    {
                        currentPosition = (Address)this.EndPosition;
                        return moved;
                    }
                    else
                    {
                        item = program.ImageMap.Items.Values[iItem];
                        currentPosition = offset > 0
                            ? item.Address
                            : item.Address + item.Size;
                    }
                }
                throw new NotImplementedException();
            }
            throw new NotImplementedException();
        }

        private Address GetAddressOfLine(ImageMapItem item, int i)
        {
            if (i == 0)
                return item.Address;
            else
                return Align(item.Address + i * BytesPerLine, BytesPerLine);
        }


        private int FindIndexOfMemoryAddress(ImageMapItem item, Address addr)
        {
            var addrStart = Align(item.Address, BytesPerLine);
            long idx = (addr - addrStart) / BytesPerLine;
            return (int) idx;
        }

        public void SetPositionAsFraction(int numer, int denom)
        {
            if (denom <= 0)
                throw new ArgumentOutOfRangeException("denom", "Denominator must be larger than 0.");
            if (numer <= 0)
            {
                currentPosition = (Address)StartPosition;
                return;
            } else if (numer >= denom)
            {
                currentPosition = (Address)EndPosition;
                return;
            }

            var targetLine = (int)(((long)numer * LineCount) / denom);
            int curLine = 0;
            foreach (var item in program.ImageMap.Items.Values)
            {
                int size;
                var bi = item as ImageMapBlock;
                if (bi != null)
                {
                    size = CountDisassembledLines(bi);
                    if (curLine + size > targetLine)
                    {
                        this.currentPosition = instructions[bi][targetLine - curLine].Address;
                        return;
                    }
                }
                else
                {
                    size = CountMemoryLines(item);
                    if (curLine + size > targetLine)
                    {
                        this.currentPosition = GetAddressOfLine(item, targetLine - curLine);
                        return;
                    }
                }
                curLine += size;
            }
            currentPosition = (Address) EndPosition;
        }
    }
}