﻿#region License
/* 
 * Copyright (C) 1999-2018 John Källén.
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
using Reko.Core.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Reko.Libraries.Microchip;


namespace Reko.Gui.Forms
{
    public class OpenAsInteractor
    {
        private IOpenAsDialog dlg;
        private PICCrownking picDB;

        public void Attach(IOpenAsDialog dlg)
        {
            this.dlg = dlg;
            dlg.Load += dlg_Load;
            dlg.BrowseButton.Click += BrowseButton_Click;
            dlg.AddressTextBox.TextChanged += AddressTextBox_TextChanged;
            dlg.RawFileTypes.TextChanged += RawFileTypes_TextChanged;
            dlg.Architectures.TextChanged += Architectures_TextChanged;
        }

        private void dlg_Load(object sender, EventArgs e)
        {
            var dcCfg = dlg.Services.RequireService<IConfigurationService>();
            PopulateRawFiles(dcCfg);
            PopulateArchitectures(dcCfg);
            PopulateCPUModels();
            PopulatePlatforms(dcCfg);
            dlg.AddressTextBox.Text = "0";
            EnableControls();
        }

        private void EnableControls()
        {
            var rawfile = ((ListOption)dlg.RawFileTypes.SelectedValue).Value as RawFileElement;
            var arch = ((ListOption)dlg.Architectures.SelectedValue)?.Value as string;
            var unknownRawFileFormat = rawfile == null;
            bool platformRequired = unknownRawFileFormat;
            bool archRequired= unknownRawFileFormat;
            bool addrRequired = unknownRawFileFormat;
            bool cpuRequired = arch?.StartsWith("pic") ?? false;
            if (!unknownRawFileFormat)
            {
                platformRequired = string.IsNullOrEmpty(rawfile.Environment);
                archRequired = string.IsNullOrEmpty(rawfile.Architecture);
                addrRequired = string.IsNullOrEmpty(rawfile.BaseAddress);
            }
            dlg.Platforms.Enabled = platformRequired;
            dlg.Architectures.Enabled = archRequired;
            dlg.CPUModels.Enabled = cpuRequired;
            dlg.AddressTextBox.Enabled = addrRequired;
            dlg.OkButton.Enabled = dlg.FileName.Text.Length > 0 || !unknownRawFileFormat;
        }

        private void PopulatePlatforms(IConfigurationService dcCfg)
        {
            var noneOption = new ListOption
            {
                Text = "(None)",
                Value = null,
            };
            var platforms = new ListOption[] { noneOption }
                .Concat(
                    dcCfg.GetEnvironments()
                    .OfType<OperatingEnvironment>()
                    .OrderBy(p => p.Description)
                    .Where(p => !string.IsNullOrEmpty(p.Name))
                    .Select(p => new ListOption { Text = p.Description, Value = p }));
            dlg.Platforms.DataSource = new ArrayList(platforms.ToArray());
        }

        private void PopulateRawFiles(IConfigurationService dcCfg)
        {
            var unknownOption = new ListOption
            {
                Text = "(Unknown)",
                Value = null,
            };
            var rawFiles = new ListOption[] { unknownOption }
                .Concat(
                    dcCfg.GetRawFiles()
                    .OfType<RawFileElement>()
                    .OrderBy(p => p.Description)
                    .Where(p => !string.IsNullOrEmpty(p.Name))
                    .Select(p => new ListOption { Text = p.Description, Value = p }));
            dlg.RawFileTypes.DataSource = new ArrayList(rawFiles.ToArray());
        }

        private void PopulateArchitectures(IConfigurationService dcCfg)
        {
            var archs = dcCfg.GetArchitectures()
                .OfType<Architecture>()
                .OrderBy(a => a.Description)
                .Select(a => new ListOption { Text = a.Description, Value = a.Name });
            dlg.Architectures.DataSource = new ArrayList(archs.ToArray());
        }

        private void PopulateCPUModels()
        {
            ArrayList cpulist = null;

            if (((ListOption)dlg.Architectures.SelectedValue).Value is string sArch)
            {
                picDB = PICCrownking.GetDB();
                if (sArch.StartsWith("pic16"))
                {
                    cpulist = new ArrayList(picDB.EnumPICList(p => p.StartsWith("PIC16")).ToArray());
                }
                if (sArch.StartsWith("pic18"))
                {
                    cpulist = new ArrayList(picDB.EnumPICList(p => p.StartsWith("PIC18")).ToArray());
                }
            }
            dlg.CPUModels.DataSource = cpulist;

        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
           var uiSvc =  dlg.Services.RequireService<IDecompilerShellUiService>();
           var fileName = uiSvc.ShowOpenFileDialog("");
            if (fileName != null)
            {
                dlg.FileName.Text = fileName;
                EnableControls();
            }
        }

        void AddressTextBox_TextChanged(object sender, EventArgs e)
        {
            EnableControls();
        }

        private void RawFileTypes_TextChanged(object sender, EventArgs e)
        {
            EnableControls();
        }

        private void Architectures_TextChanged(object sender, EventArgs e)
        {
            PopulateCPUModels();
            EnableControls();
        }

    }
}

