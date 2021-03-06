﻿/*
 * Keenou
 * Copyright (C) 2015  Charles Munson
 * 
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License along
 * with this program; if not, write to the Free Software Foundation, Inc.,
 * 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA.
*/

using System;
using System.IO;

namespace Keenou
{
    static class Config
    {

        // Constants pre-defined 
        public static readonly int MASTERKEY_PW_CHAR_COUNT = 100;              // Character count for master key password 
        public static readonly int VOLUME_SIZE_MULT_DEFAULT = 2;               // Suggest volume size should be this times larger than current est. home directory size 
        public static readonly int MIN_PASSWORD_LEN = 6;                       // Minimum password length 
        public static readonly string ENCFS_CONFIG_FILENAME = ".encfs6.xml";   // Filename for the EncFS config file 

        // Determine where (x86) programs are installed 
        public static readonly string x86ProgramDirectory = (Environment.GetEnvironmentVariable("PROGRAMFILES(X86)") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        public static readonly string KeenouProgramDirectory = Path.Combine(x86ProgramDirectory, @"Keenou\");

        // Software identifiers
        public enum Software { VeraCrypt, EncFS, Dokan };

        // All possible ciphers and hashes supported (and defaults) 
        public static readonly string[] CIPHERS_S = { "AES", "Serpent", "Twofish", "AES(Twofish)", "AES(Twofish(Serpent))", "Serpent(AES)", "Serpent(Twofish(AES))", "Twofish(Serpent)" };
        public static readonly string[] HASHES_S = { "sha256", "sha512", "whirlpool", "ripemd160" };
        public enum Ciphers { AES, SERPENT, TWOFISH, AES_TWOFISH, AES_TWOFISH_SERPENT, SERPENT_AES, SERPENT_TWOFISH_AES, TWOFISH_SERPENT };
        public enum Hashes { SHA256, SHA512, WHIRLPOOL, RIPEMD160 };
        public static readonly Ciphers CIPHER_C_DEFAULT = Ciphers.AES;          // Drop-down index for default cipher in ENC HOME UI 
        public static readonly Hashes HASH_C_DEFAULT = Hashes.WHIRLPOOL;        // Drop-down index for default hash in ENC HOME UI 

        // Cloud service providers supported 
        public enum Clouds { Dropbox, GoogleDrive, OneDrive };

        // Registry key roots 
        public static readonly string LOCAL_MACHINE_REG_ROOT = @"HKEY_LOCAL_MACHINE\SOFTWARE\Keenou\";
        public static readonly string CURR_USR_REG_ROOT = @"HKEY_CURRENT_USER\Software\Keenou\";
        public static readonly string CURR_USR_REG_DRIVE_ROOT = @"HKEY_CURRENT_USER\Software\Keenou\drives\";



    }  // End Config class 

    // End namespace 
}
