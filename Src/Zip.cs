using System.IO;
using System.IO.Compression;

namespace Bender
{
    class Zip
    {
        public static void Download(string path, Stream output)
        {
            using (var fs = File.OpenRead(path))
            {
                fs.CopyTo(output);
            }
        }

        public static void Upload(string path, Stream input)
        {
            using (var fs = File.OpenWrite(path))
            {
                input.CopyTo(fs);
                fs.SetLength(fs.Position);
            }
        }

        public static void Zipit(string path, Stream output)
        {
            var temp = Path.GetTempFileName();
            File.Delete(temp);
            ZipFile.CreateFromDirectory(path, temp);
            using (var fs = File.OpenRead(temp))
            {
                fs.CopyTo(output);
            }
            File.Delete(temp);
        }

        public static void Unzipit(string path, Stream input)
        {
            using (var za = new ZipArchive(input, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry file in za.Entries)
                {
                    string fileName = Path.Combine(path, file.FullName);

                    if (string.IsNullOrEmpty(file.Name))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(fileName));
                    }
                    else
                    {
                        file.ExtractToFile(fileName, true);
                    }
                }
            }
        }
    }
}
