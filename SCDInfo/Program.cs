using System;
using System.IO;
using System.Collections.Generic;
namespace SCDInfo
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length > 1)
            {
                string command = args[0];
                string inputSCD = args[1];
                byte[] oldSCD = File.ReadAllBytes(inputSCD);
                uint tables_offset = Read(oldSCD, 16, 0x0e);
                uint true_entries = Read(oldSCD, 16, (int)tables_offset + 0x02);
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
                if (command.Equals("info"))
                {
                    Console.WriteLine("Number of Tracks: " + true_entries);
                    if (codec == 6)
                    {
                        Console.WriteLine("The Codec used is ogg/vorbis");
                    }
                    else if (codec == 12)
                    {
                        Console.WriteLine("The Codec used is masadpcm");
                    }
                    Console.WriteLine();
                }
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
                            newEntry = GetOGGInfo(entry, command, i);
                        }
                        else
                        {
                            newEntry = GetMSADPCMInfo(entry, command, i);
                        }
                    }
                    else
                    {
                        dummy_entries = dummy_entries + 1;
                        newEntry = entry;
                    }
                    if (command.Equals("decrypt"))
                    {
                        SCDs.Add(newEntry);
                        file_size = file_size + newEntry.Length;
                        entry_offsets[i + 1] = file_size;
                    }
                        
                }
                if (command.Equals("decrypt"))
                {
                    if (codec == 0x6)
                    {
                        byte[] finalSCD = new byte[file_size];
                        Array.Copy(oldSCD, finalSCD, entry_offsets[0]);
                        //Write new headers table
                        for (int i = 0; i < headers_entries; i++)
                        {
                            Array.Copy(SCDs[i], 0, finalSCD, entry_offsets[i], SCDs[i].Length);
                        }
                        string outputPath = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), $"decrypted.scd");
                        File.WriteAllBytes(outputPath, finalSCD);
                    }
                    else
                    {
                        Console.WriteLine("msadpcm SCDs are not encrypted, the decrypt command doesnt do anything to them");
                    }
                    
                }                    
            }
            else
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("SCDInfo info|extract|decrypt <File/Dir>");
            }
        }

        static byte[] GetOGGInfo(byte[] entry, string command, int i)
        {                            
            uint meta_offset = 0;
            uint stream_size = Read(entry, 32, (int)meta_offset + 0x00);
            uint channel_count = Read(entry, 32, (int)meta_offset + 0x04);
            uint sample_rate = Read(entry, 32, (int)meta_offset + 0x08);
            uint loop_start = Read(entry, 32, (int)meta_offset + 0x10);
            uint loop_end = Read(entry, 32, (int)meta_offset + 0x14);
            uint aux_chunk_count = Read(entry, 32, (int)meta_offset + 0x1c);
            uint extradata_offset = meta_offset + 0x20;
            uint mark_entries = 0;
            uint[] marks = new uint[9]; 
            if (aux_chunk_count > 0)
            {
                extradata_offset = extradata_offset + Read(entry, 32, (int)extradata_offset + 0x04);
                mark_entries = Read(entry, 32, (int)meta_offset + 0x30);
                for (int j=0; j < mark_entries; j++)
                {
                    marks[j] = Read(entry, 32, (int)meta_offset + 0x34 + j*0x04);
                }
            }
            uint encryption_key = Read(entry, 8, (int)extradata_offset + 0x02);
            uint seek_table_size = Read(entry, 32, (int)extradata_offset + 0x10);
            uint vorb_header_size = Read(entry, 32, (int)extradata_offset + 0x14);
            uint start_offset = extradata_offset + 0x20 + seek_table_size;
            if (command.Equals("info"))
            {
                Console.WriteLine("Track Number " + (i + 1) + ":");
                Console.WriteLine("OGG File Size: " + stream_size + " Bytes");
                Console.WriteLine("Number Of Channels: " + channel_count);
                Console.WriteLine("Sample_Rate: " + sample_rate);
                Console.WriteLine("Encryption Key: " + encryption_key.ToString("X"));
                if (loop_end > 0)
                {
                    if (loop_start == 0)
                    {
                        Console.WriteLine("Track Is Looped: Full Loop ");
                    }
                    else
                    {
                        Console.WriteLine("Track Is Looped: Custom Loop ");
                    }
                }
                else
                {
                    Console.WriteLine("Track Is Not Looped");
                }
                Console.WriteLine("Track Has " + mark_entries + " MARK Table Entries");
                if (mark_entries > 0)
                for (int j = 0; j < mark_entries; j++)
                {
                    Console.WriteLine("MARK" + j + ": sample " + marks[j]);
                }
                Console.WriteLine();
            }                               
            if (command.Equals("decrypt") || command.Equals("extract"))
            {
                Decrypt(entry, (int)encryption_key, (int)start_offset, (int)start_offset + (int)vorb_header_size);
                Write(entry, 0, 8, (int)extradata_offset + 0x02);
            }
            if (command.Equals("extract"))
            {
                uint ogg_size = vorb_header_size + stream_size;
                byte[] ogg = new byte[ogg_size];
                Array.Copy(entry, start_offset, ogg, 0, ogg_size);
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), (i + 1) + ".ogg"), ogg);
            }                          
            return entry;
        }

        static byte[] GetMSADPCMInfo(byte[] entry, string command, int i)
        {
            uint meta_offset = 0;
            uint stream_size = Read(entry, 32, (int)meta_offset + 0x00);
            uint channel_count = Read(entry, 32, (int)meta_offset + 0x04);
            uint sample_rate = Read(entry, 32, (int)meta_offset + 0x08);
            uint extradata_offset = meta_offset + 0x20;
            uint start_offset = extradata_offset + 0x32;
            if (command.Equals("info"))
            {
                Console.WriteLine("Track Number " + (i + 1) + ":");
                Console.WriteLine("MSADPCM Data Size: " + stream_size + " Bytes");
                Console.WriteLine("Number Of Channels: " + channel_count);
                Console.WriteLine("Sample_Rate: " + sample_rate);
            }
            if (command.Equals("extract"))
            {
                byte[] msadpcm = new byte[stream_size + 0x4e];
                Write(msadpcm, 0x46464952, 32, 0);
                Write(msadpcm, msadpcm.Length - 0x08, 32, 0x04);
                Write(msadpcm, 0x45564157, 32, 0x08);
                Write(msadpcm, 0x20746D66, 32, 0x0c);
                Write(msadpcm, 0x32, 32, 0x10);
                Array.Copy(entry, extradata_offset, msadpcm, 0x14, 0x32);
                Write(msadpcm, 0x61746164, 32, 0x46);
                Write(msadpcm, (int)stream_size, 32, 0x4a);
                Array.Copy(entry, start_offset, msadpcm, 0x4e, stream_size);
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), (i + 1) + ".wav"), msadpcm);
            }
            return entry;
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
                entry_codec = Read(data, 32, (int)entry_begin);
                if (entry_codec != 0xFFFFFFFF)
                {
                    break;
                }
            }
            return entry_codec;
        }

        static void Decrypt(byte[] file, int encryption_key, int starting_position, int end_position)
        {
            byte[] key = BitConverter.GetBytes(encryption_key);
            for (int i = starting_position; i < end_position; i++)
            {
                file[i] = (byte)(file[i]^key[0]);
            }
        }
    }
}
