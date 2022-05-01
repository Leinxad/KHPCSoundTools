using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
namespace MultiEncoder
{
    class Program
    {
        static readonly string TOOLS_PATH = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), "tools");

        static void Main(string[] args)
        {
            if (!Directory.Exists(Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), "output")))
            {
                Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), "output"));
            }
            if (!File.Exists(@$"{TOOLS_PATH}/oggenc/oggenc.exe"))
            {
                Console.WriteLine($"Please put oggenc.exe in the tools/oggenc folder: {TOOLS_PATH}/oggenc");
                return;
            }
            if (!File.Exists(@$"{TOOLS_PATH}/adpcmencode3/adpcmencode3.exe"))
            {
                Console.WriteLine($"Please put adpcmencode3.exe in the tools folder: {TOOLS_PATH}/adpcmencode3");
                return;
            }
            if (args.Length > 0)
            {
                string inputSCD = args[0];
                int Quality = 10;
                if (args.Length > 1)
                {
                    Quality = Convert.ToInt32(args[1]);
                }
                byte[] oldSCD = File.ReadAllBytes(inputSCD);
                uint tables_offset = Read(oldSCD, 16, 0x0e);
                uint headers_entries = Read(oldSCD, 16, (int)tables_offset + 0x04);
                uint headers_offset = Read(oldSCD, 32, (int)tables_offset + 0x0c);
                int file_size = (int)Read(oldSCD, 32, (int)headers_offset);
                List<byte[]> SCDs = new();
                uint entry_begin;
                uint entry_end;
                uint entry_size;
                int dummy_entries = 0;
                byte[] newEntry;
                int[] entry_offsets = new int[headers_entries + 1];
                entry_offsets[0] = file_size;
                uint codec = getCodec(headers_entries, headers_offset, oldSCD);
                
                List<int> wavDurations = new List<int>();
                for (int i = 0; i < headers_entries; i++)
                {
                    entry_begin = Read(oldSCD, 32, (int)headers_offset + i * 0x04);
                    if (i == headers_entries - 1)
                    {
                        entry_end = (uint)oldSCD.Length;
                    }
                    else
                    {
                        entry_end = Read(oldSCD, 32, (int)headers_offset + (i + 1) * 0x04);
                    }
                    entry_size = entry_end - entry_begin;
                    byte[] entry = new byte[entry_size];
                    Array.Copy(oldSCD, entry_begin, entry, 0, entry_size);
                    //Check if entry is dummy
                    if (Read(entry, 32, 0x0c) != 0xFFFFFFFF)
                    {
                        if (codec == 0x6)
                        {
                            string wavpath = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), (i + 1 - dummy_entries) + ".wav");
                            byte[] wav = File.ReadAllBytes(wavpath);
                            //Get wav duration
                            uint diviser = Read(wav, 32, 0x1c);
                            uint dataSize = Read(wav, 32, 0x28);
                            float duration = (dataSize * 1000) / diviser;
                            wavDurations.Add((int)Math.Ceiling(duration));
                            
                            //Get Loop Points from Tags
                            int LoopStart_Sample = searchTag("LoopStart", wav);
                            int Total_Samples = searchTag("LoopEnd", wav);
                            WavtoOGG(wavpath, LoopStart_Sample, Total_Samples, Quality);
                            string oggPath = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), $"{Path.GetFileNameWithoutExtension(wavpath)}.ogg");
                            newEntry = OGGtoSCD(wav, entry, oggPath, LoopStart_Sample, Total_Samples);
                        }
                        else
                        {
                            string wavpath = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), (i + 1 - dummy_entries) + ".wav");
                            byte[] wav = File.ReadAllBytes(wavpath);
                            WavtoMSADPCM(wavpath);
                            string msadpcmPath = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory),"adpcm" + $"{Path.GetFileNameWithoutExtension(wavpath)}.wav");
                            newEntry = MSADPCMtoSCD(wav, entry, msadpcmPath);
                        }
                    }
                    else
                    {
                        dummy_entries = dummy_entries + 1;
                        newEntry = entry;
                    }
                    SCDs.Add(newEntry);
                    file_size = file_size + newEntry.Length;
                    entry_offsets[i + 1] = file_size;
                }
                byte[] finalSCD = new byte[file_size];
                Array.Copy(oldSCD, finalSCD, entry_offsets[0]);
                //Write new headers table
                for (int i = 0; i < headers_entries; i++)
                {
                    Write(finalSCD, entry_offsets[i], 32, (int)headers_offset + i * 0x04);
                    Array.Copy(SCDs[i], 0, finalSCD, entry_offsets[i], SCDs[i].Length);
                }
                //update OGG durations
                if (Read(oldSCD, 32, 0x08) <= 3)
                {
                    uint table1_count = Read(oldSCD, 16, (int)tables_offset);
                    uint table2_count = Read(oldSCD, 16, (int)tables_offset + 0x02);
                    uint table2_offset = Read(oldSCD, 32, (int)tables_offset + 0x08);
                    int table1_offset = (int)tables_offset + 0x20;
                    for (int i = 0; i < table1_count; i++)
                    {
                        uint offset = Read(oldSCD, 32, table1_offset + i * 4);
                        uint unk = Read(oldSCD, 16, (int)offset);
                        if (unk != 256)
                        {
                            uint scdIndex = Read(oldSCD, 16, (int)offset + 0x10);
                            uint wavIndex = Read(oldSCD, 16, (int)offset + 0x12);
                            
                            uint durationOffset = Read(oldSCD, 32, (int)(table2_offset + scdIndex * 4));
                            int durationMSoffset = (int)durationOffset + 0x50;
                            Console.WriteLine(scdIndex + " plays " + (wavIndex + 1) + ".wav [" + Read(oldSCD, 32, (int)durationMSoffset) + "ms] will be updated to [" + wavDurations[(int)wavIndex] + "ms]");
                            Write(finalSCD, wavDurations[(int)wavIndex], 32, durationMSoffset);
                        }
                    }
                }
                //Write File Size            
                Write(finalSCD, file_size, 32, 0x10);
                string outputPath = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), "output", $"{Path.GetFileNameWithoutExtension(inputSCD)}.scd");
                File.WriteAllBytes(outputPath, finalSCD);
            }
            else
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("MultiEncoder <InputSCD/Dir>");
            }

            static void WavtoOGG(string inputWAV, int LoopStart_Sample, int Total_Samples, int Quality)
            {
                Process p = new Process();
                p.StartInfo.FileName = $@"{TOOLS_PATH}/oggenc/oggenc.exe";
                if (LoopStart_Sample == -1 && Total_Samples == -1)
                {
                    p.StartInfo.Arguments = $" \"{inputWAV}\" -s 0 -q \"{Quality}\"";
                }
                else
                {
                    p.StartInfo.Arguments = $" \"{inputWAV}\" -s 0 -q \"{Quality}\" -c LoopStart=\"{LoopStart_Sample}\" -c LoopEnd=\"{Total_Samples - 1}\"";
                }
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardInput = false;
                p.Start();
                p.WaitForExit();
            }

            static byte[] OGGtoSCD(byte[] wav, byte[] entry, string oggPath, int LoopStart_Sample, int Total_Samples)
            {
                byte[] ogg = File.ReadAllBytes(oggPath);
                uint meta_offset = 0;
                uint extradata_offset = meta_offset + 0x20;
                //Find Vorbis Header Size
                int vorbis_header_size = 0;
                byte[] pattern = new byte[] { 0x05, 0x76, 0x6F, 0x72, 0x62, 0x69, 0x73 };
                vorbis_header_size = SearchBytePattern(vorbis_header_size, ogg, pattern);
                pattern = new byte[] { 0x4F, 0x67, 0x67, 0x53 };
                while (true)
                {
                    vorbis_header_size = SearchBytePattern(vorbis_header_size, ogg, pattern);
                    if (Read(ogg, 8, vorbis_header_size + 0x05) != 1)
                    {
                        break;
                    }
                    vorbis_header_size = vorbis_header_size + 4;
                }
                //Find OGG Pages Offsets
                List<int> page_offsets = new();
                int offset = vorbis_header_size;
                pattern = new byte[] { 0x4F, 0x67, 0x67, 0x53 };
                while (true)
                {
                    offset = SearchBytePattern(offset, ogg, pattern);
                    if (offset == -1)
                    {
                        break;
                    }
                    page_offsets.Add(offset);
                    offset = offset + 4;
                }
                //Write Stream Size
                int streamSize = ogg.Length - vorbis_header_size;
                Write(entry, streamSize, 32, (int)meta_offset);
                //Find Loop Offsets
                int LoopStart = 0;
                int LoopEnd = 0;
                if (Total_Samples != -1)
                {
                    LoopEnd = streamSize;
                }
                if (LoopStart_Sample != -1)
                {
                    for (int i = 0; i < page_offsets.Count; i++)
                    {
                        offset = page_offsets[i];
                        if (LoopStart_Sample <= Read(ogg, 32, offset + 6))
                        {
                            LoopStart = page_offsets[i] - vorbis_header_size;
                            break;
                        }
                    }
                }
                //Write LoopStart and LoopEnd
                Write(entry, LoopStart, 32, (int)meta_offset + 0x10);
                Write(entry, LoopEnd, 32, (int)meta_offset + 0x14);
                //Write Channels
                uint Channels = Read(ogg, 8, 0x27);
                Write(entry, (int)Channels, 8, (int)meta_offset + 0x04);
                //Write Sample Rate
                uint Sample_Rate = Read(ogg, 32, 0x28);
                Write(entry, (int)Sample_Rate, 32, (int)meta_offset + 0x08);
                //Read Aux Info
                uint aux_chunk_count = Read(entry, 32, (int)meta_offset + 0x1c);
                uint aux_chunk_size = 0;
                if (aux_chunk_count > 0)
                {
                    aux_chunk_size = Read(entry, 32, (int)extradata_offset + 0x04);
                    extradata_offset = extradata_offset + aux_chunk_size;
                    uint mark_entries = Read(entry, 32, (int)meta_offset + 0x30);
                    //Write Aux Info
                    Write(entry, LoopStart_Sample, 32, (int)meta_offset + 0x28);
                    Write(entry, Total_Samples, 32, (int)meta_offset + 0x2C);
                    if (mark_entries == 1)
                    {
                        int mark = searchTag("MARK1", wav);
                        if (mark != -1)
                        {
                            Write(entry, mark, 32, (int)meta_offset + 0x34);
                        }
                        else
                        {
                            Write(entry, LoopStart_Sample, 32, (int)meta_offset + 0x34);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < mark_entries; i++)
                        {
                            int mark = searchTag("MARK" + (i + 1), wav);
                            if (mark != -1)
                            {
                                Write(entry, mark, 32, (int)meta_offset + 0x34 + i * 0x04);
                            }
                            else
                            {
                                Write(entry, 0, 32, (int)meta_offset + 0x34 + i * 0x04);
                            }
                        }
                    }
                }

                //Write Vorbis Header Size
                Write(entry, vorbis_header_size, 32, (int)extradata_offset + 0x14);
                //Set Encryption Key to 0
                Write(entry, 0x00, 8, (int)extradata_offset + 0x02);
                //Create Seek Table                
                offset = vorbis_header_size;
                List<int> seek_offsets = new();
                uint previous_granule = Read(ogg, 32, offset + 0x06);
                uint current_granule;
                seek_offsets.Add(0);
                if (offset != page_offsets[page_offsets.Count - 1])
                {
                    for (int i = 1; i < page_offsets.Count; i++)
                    {
                        offset = page_offsets[i];
                        if (i == page_offsets.Count - 1)
                        {
                            break;
                        }
                        current_granule = Read(ogg, 32, offset + 0x06);
                        if (current_granule - previous_granule >= 2048)
                        {
                            seek_offsets.Add(offset - vorbis_header_size);
                            previous_granule = current_granule;
                        }
                    }
                    if (seek_offsets.Count == page_offsets.Count - 1)
                    {
                        seek_offsets.Add(offset - vorbis_header_size);
                    }
                }
                byte[] seek_table = new byte[seek_offsets.Count * 4];
                for (int i = 0; i < seek_offsets.Count; i++)
                {
                    Write(seek_table, seek_offsets[i], 32, i * 4);
                }
                //Write Seek Table Size
                Write(entry, seek_table.Length, 32, (int)extradata_offset + 0x10);
                //Write Extradata Size
                Write(entry, 0x20 + vorbis_header_size + (int)aux_chunk_size + seek_table.Length, 32, (int)meta_offset + 0x18);
                int file_size = (int)(extradata_offset + 0x20 + seek_table.Length + ogg.Length);
                while (file_size % 16 != 0)
                {
                    file_size = file_size + 1;
                }
                byte[] newEntry = new byte[file_size];
                Array.Copy(entry, newEntry, extradata_offset + 0x20);
                Array.Copy(seek_table, 0, newEntry, extradata_offset + 0x20, seek_table.Length);
                Array.Copy(ogg, 0, newEntry, extradata_offset + 0x20 + seek_table.Length, ogg.Length);
                File.Delete(oggPath);
                return newEntry;
            }

            static void WavtoMSADPCM(string inputWAV)
            {
                string outputWAV = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), "adpcm" + $"{Path.GetFileNameWithoutExtension(inputWAV)}.wav");
                Process p = new Process();
                p.StartInfo.FileName = $@"{TOOLS_PATH}/adpcmencode3/adpcmencode3.exe";
                p.StartInfo.Arguments = $" \"{inputWAV}\" \"{outputWAV}\"";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardInput = false;
                p.Start();
                p.WaitForExit();
            }

            static byte[] MSADPCMtoSCD(byte[] wav, byte[] entry, string msadpcmPath)
            {
                byte[] msadpcm = File.ReadAllBytes(msadpcmPath);
                uint meta_offset = 0;
                uint extradata_offset = meta_offset + 0x20;
                uint Channels = Read(msadpcm, 8, 0x16);
                uint Sample_Rate = Read(msadpcm, 32, 0x18);
                Write(entry, (int)Channels, 8, (int)meta_offset + 0x04);
                Write(entry, (int)Sample_Rate, 32, (int)meta_offset + 0x08);
                byte[] pattern = new byte[] { 0x64, 0x61, 0x74, 0x61 };
                int data_offset = SearchBytePattern(0, msadpcm, pattern) + 8;
                Write(entry, msadpcm.Length - data_offset, 32, (int)meta_offset);
                int file_size = (int)(extradata_offset + 0x32 + msadpcm.Length - data_offset);
                while (file_size % 16 != 0)
                {
                    file_size = file_size + 1;
                }
                byte[] newEntry = new byte[file_size];
                Array.Copy(entry, newEntry, extradata_offset);
                Array.Copy(msadpcm, 0x14, newEntry, extradata_offset, 0x32);
                Array.Copy(msadpcm, data_offset, newEntry, extradata_offset + 0x32 , msadpcm.Length - data_offset);
                File.Delete(msadpcmPath);
                return newEntry;
            }

            static uint Read(byte[] file, int bits, int position)
            {
                int bytes = bits / 8;
                byte[] value = new byte[bytes];
                for (int i = 0; i < bytes; i++)
                {
                    value[i] = file[position + i];
                }
                uint num;
                if (bytes == 1)
                {
                    num = value[0];
                    return num;

                }
                else if (bytes == 2)
                {
                    num = BitConverter.ToUInt16(value, 0);
                    return num;
                }
                else if (bytes == 4)
                {
                    num = BitConverter.ToUInt32(value, 0);
                    return num;
                }
                else
                {
                    return 0;
                }
            }

            static void Write(byte[] file, int value, int bits, int position)
            {
                int bytes = bits / 8;
                byte[] val;
                val = BitConverter.GetBytes((uint)value);
                for (int i = 0; i < bytes; i++)
                {
                    file[position + i] = val[i];
                }
            }

            static uint getCodec(uint headers_entries, uint headers_offset, byte[] data)
            {
                uint entry_begin;
                uint entry_codec = 0xFFFFFFFF;
                for (int i = 0; i < headers_entries; i++)
                {
                    entry_begin = Read(data, 32, (int)headers_offset + i * 0x04);
                    entry_codec = Read(data, 32, (int)entry_begin + 0x0c);
                    if (entry_codec != 0xFFFFFFFF)
                    {
                        break;
                    }
                }
                return entry_codec;
            }

            static int SearchBytePattern(int position, byte[] data, byte[] pattern)
            {
                int patternLength = pattern.Length;
                int totalLength = data.Length;
                byte firstMatchByte = pattern[0];
                for (int i = position; i < totalLength; i++)
                {
                    if (firstMatchByte == data[i] && totalLength - i >= patternLength)
                    {
                        byte[] match = new byte[patternLength];
                        Array.Copy(data, i, match, 0, patternLength);
                        if (match.SequenceEqual<byte>(pattern))
                        {
                            return i;
                        }
                    }
                }
                return -1;
            }

            static int searchTag(string tag, byte[] data)
            {
                byte[] pattern = Encoding.ASCII.GetBytes(tag);
                int value = SearchBytePattern(0, data, pattern);
                if (value != -1)
                {
                    value = value + pattern.Length;
                    value = getTagData(value, data);
                }
                return value;
            }

            static int getTagData(int position, byte[] data)
            {
                while (data[position] - 0x30 < 0 || data[position] - 0x30 > 9)
                {
                    position = position + 1;

                }
                int initial_position = position;
                while (data[position] - 0x30 >= 0 && data[position] - 0x30 <= 9)
                {
                    position = position + 1;
                    if (position == data.Length)
                    {
                        break;
                    }
                }
                byte[] number = new byte[position - initial_position];
                for (int i = 0; i < number.Length; i++)
                {
                    number[i] = data[i + initial_position];
                }
                string value = System.Text.Encoding.ASCII.GetString(number);
                int tagData = Convert.ToInt32(value);
                return tagData;
            }
        }
    }
}
