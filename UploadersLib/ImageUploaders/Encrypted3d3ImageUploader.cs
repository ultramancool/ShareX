﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using HelpersLib;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using UploadersLib.HelperClasses;

namespace UploadersLib.ImageUploaders
{

    public sealed class Encrypted3d3ImageUploader : ImageUploader
    {
        private const int MacSize = 64;
        private const string ApiKey = "c61540b5ceecd05092799f936e27755f";
        private const string SystemUrl = "https://e.3d3.ca";

        public Encrypted3d3ImageUploader()
        {

        }

        public static string UrlBase64Encode(byte[] input)
        {
            return Convert.ToBase64String(input).Replace("=", "").Replace("+", "-").Replace("/", "_");
        }

        public static Stream Encrypt(Stream stream, out string seed_encoded, out string ident_encoded)
        {
            RNGCryptoServiceProvider rngCsp = new RNGCryptoServiceProvider();
            byte[] seed = new byte[16];
            rngCsp.GetBytes(seed);
            seed_encoded = UrlBase64Encode(seed);
            
            SHA512CryptoServiceProvider sha512csp = new SHA512CryptoServiceProvider();
            byte[] seed_result = sha512csp.ComputeHash(seed);
            byte[] key = new byte[32];
            Buffer.BlockCopy(seed_result, 0, key, 0, 32);

            byte[] iv = new byte[16];
            Buffer.BlockCopy(seed_result, 32, iv, 0, 16);

            byte[] ident = new byte[16];
            Buffer.BlockCopy(seed_result, 48, ident, 0, 16);
            ident_encoded = UrlBase64Encode(ident);

            byte[] rawdata = stream.GetBytes();

            int l = FindIVLen(rawdata.Length);
            byte[] civ = new byte[l];
            Array.Copy(iv, civ, l);
            KeyParameter key_param = new KeyParameter(key);
            var ccmparams = new CcmParameters(key_param, MacSize, civ, new byte[0]);
            var ccmMode = new CcmBlockCipher(new AesFastEngine());
            ccmMode.Init(true, ccmparams);
            var encBytes = new byte[ccmMode.GetOutputSize(rawdata.Length)];
            var res = ccmMode.ProcessBytes(rawdata, 0, rawdata.Length, encBytes, 0);
            ccmMode.DoFinal(encBytes, res);

            return new MemoryStream(encBytes);
        }

        private static int FindIVLen(int bufferLength)
        {
            if (bufferLength < 0xFFFF) return 15 - 2;
            if (bufferLength < 0xFFFFFF) return 15 - 3;
            return 15 - 4;
        }


        public override UploadResult Upload(Stream stream, string fileName)
        {
            string seed, ident;
            Stream encryptedStream = Encrypt(stream, out seed, out ident);
            Dictionary<string,string> args = new Dictionary<string, string>();
            args["ident"] = ident;
            args["privkey"] = ApiKey;
            UploadResult result = UploadData(encryptedStream, SystemUrl + "/up", "blob", "file",  args);

            if (result.IsSuccess)
            {
                Dictionary<string, string> values = JsonConvert.DeserializeObject<Dictionary<string, string>>(result.Response);
                result.URL = SystemUrl + "/#/" + seed;
                result.DeletionURL = SystemUrl + "/del?ident=" + ident + "&delkey=" + values["delkey"];
            }

            return result;
        }
    }
}
