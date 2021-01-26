using System.IO;
using ArcSysAPI.Common.Enums;
using ArcSysAPI.Utils;

namespace GeoArcSysCryptTool
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var file = @"C:\Users\Administrator\Downloads\char_am_img.pac";

            using (var fileStream = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                using (var stream =
                    BBObfuscatorTools.DFASFPACDeflateStream(fileStream, true))
                {
                    using (var decryptStream =
                        BBObfuscatorTools.FPACCryptStream(stream, file, CryptMode.Encrypt))
                    {
                        fileStream.Close();
                        File.WriteAllBytes(file, decryptStream.ToArray());
                        decryptStream.Close();
                    }
                }
                
            }
        }
    }
}