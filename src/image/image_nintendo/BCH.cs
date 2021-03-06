﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Kontract.Image;
using Kontract.Interface;
using Kontract.Image.Format;
using Kontract.Image.Swizzle;
using Kontract.IO;
using Cetera.PICA;

//Code ported from Ohana3DS

namespace image_nintendo.BCH
{
    public sealed class BCH
    {
        public List<Bitmap> bmps = new List<Bitmap>();
        public List<ImageSettings> _settings = new List<ImageSettings>();

        private Header header;
        private List<PICACommandReader> picaEntries = new List<PICACommandReader>();
        private List<TexEntry> origValues = new List<TexEntry>();

        byte[] _file = null;

        public BCH(string filename)
        {
            using (BinaryReaderX br = new BinaryReaderX(File.OpenRead(filename)))
            {
                _file = br.ReadAllBytes();

                //Header
                header = new Header(br.BaseStream);

                if (header.dataSize != 0)
                {
                    //get entryCount
                    br.BaseStream.Position = header.gpuCommandsOffset;
                    int entryCount = 0;
                    List<uint> wordCount = new List<uint>();
                    wordCount.Add(0);
                    using (var br2 = new BinaryReaderX(new MemoryStream(br.ReadBytes((int)header.gpuCommandsSize))))
                    {
                        while (br2.BaseStream.Position < br2.BaseStream.Length - 2)
                        {
                            br2.BaseStream.Position += 4;
                            var comp = br2.ReadUInt16();
                            wordCount[wordCount.Count - 1] += 2;
                            if (comp == 0x23d)
                            {
                                entryCount++;
                                wordCount.Add(0);
                            }
                            br2.BaseStream.Position += 2;
                        }
                    }

                    //get commands
                    br.BaseStream.Position = header.gpuCommandsOffset;
                    for (int i = 0; i < entryCount; i++)
                    {
                        picaEntries.Add(new PICACommandReader(br.BaseStream, wordCount[i]));
                    }

                    //loop through commandReaders to get textures
                    for (int i = 0; i < entryCount; i++)
                    {
                        var size = picaEntries[i].getTexUnit0Size();
                        if (size.Height != 0 && size.Width != 0)
                        {
                            br.BaseStream.Position = header.dataOffset + picaEntries[i].getTexUnit0Address();
                            var format = (byte)picaEntries[i].getTexUnit0Format();
                            int bitDepth = Support.CTRFormat[format].BitDepth;

                            for (int j = 0; j <= picaEntries[i].getTexUnit0LoD(); j++)
                            {
                                origValues.Add(new TexEntry(size.Width >> j, size.Height >> j, format));

                                var settings = new ImageSettings
                                {
                                    Width = size.Width >> j,
                                    Height = size.Height >> j,
                                    Format = Support.CTRFormat[format],
                                    Swizzle = new CTRSwizzle(size.Width >> j, size.Height >> j)
                                };

                                _settings.Add(settings);
                                bmps.Add(Common.Load(br.ReadBytes((((size.Width >> j) * (size.Height >> j)) * bitDepth) / 8), settings));
                            }

                            br.BaseStream.Position = (br.BaseStream.Position + 0x7f) & ~0x7f;
                        }
                    }
                }
            }
        }

        public void Save(string filename)
        {
            for (int i = 0; i < bmps.Count(); i++)
            {
                if (origValues[i].width != bmps[i].Width || origValues[i].height != bmps[i].Height)
                    throw new Exception("All BCH textures have to be the same size as the original!");
            }

            using (BinaryWriterX bw = new BinaryWriterX(File.Create(filename)))
            {
                bw.Write(_file);

                int bmpCount = 0;
                foreach (var picaEntry in picaEntries)
                {
                    var size = picaEntry.getTexUnit0Size();
                    if (size.Height != 0 && size.Width != 0)
                    {
                        bw.BaseStream.Position = header.dataOffset + picaEntry.getTexUnit0Address();
                        var format = (byte)picaEntry.getTexUnit0Format();

                        for (int j = 0; j <= picaEntry.getTexUnit0LoD(); j++)
                        {
                            var settings = new ImageSettings
                            {
                                Width = size.Width >> j,
                                Height = size.Height >> j,
                                Format = Support.CTRFormat[format],
                                Swizzle = new CTRSwizzle(size.Width >> j, size.Height >> j)
                            };

                            bw.Write(Common.Save(bmps[bmpCount++], settings));
                        }
                    }
                }
            }
        }
    }
}
