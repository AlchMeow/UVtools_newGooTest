using BinarySerialization;
using Emgu.CV;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using EmguExtensions;
using UVtools.Core.Exceptions;
using UVtools.Core.Extensions;
using UVtools.Core.Layers;
using UVtools.Core.Operations;
using ZLinq;

namespace UVtools.Core.FileFormats;

/// <summary>
/// Elegoo GOO V5 file format.
/// V5 uses index tables (LDT/IEDT/RDT), partitioned images, VUF RLE codec,
/// and an MD5 checksum. The header is binary-compatible with V3 through
/// field 61, then adds 6 extension fields.
/// </summary>
public sealed class GooV5File : FileFormat
{
    #region Constants

    private const string FileVersion = "V5.1";

    private static readonly byte[] FileMagic =
    [
        0x07, 0x00, 0x00, 0x00,
        0x44, 0x4C, 0x50, 0x00
    ];

    private static byte[] Delimiter => [0x0D, 0x0A];

   private static byte LayerMagic => 0x55;

   private static byte ResinDataMagic => 0x66;

   private static byte LdtMagic => 0xA1;
   private static byte IedtMagic => 0xA2;
   private static byte RdtMagic => 0xA3;
   private static byte EdtMagic => 0xA4;

   private const byte VufRunOp  = 0x40;
    private const byte VufGrayOp = 0x00;
    private const byte VufDiffOp = 0x80;

    #endregion

    #region Enums

    public enum DelayModes : byte
    {
        LightOff = 0,
        WaitTime = 1
    }

    #endregion

    #region Sub Classes

    public class FileHeader
    {
        [FieldOrder(0)] [FieldLength(4)] public string Version { get; set; } = FileVersion;
        [FieldOrder(1)] [FieldCount(8)] public byte[] Magic { get; set; } = FileMagic;

        [FieldOrder(2)]
        [FieldLength(32)]
        [SerializeAs(SerializedType.TerminatedString)]
        public string SoftwareName { get; set; } = About.Software;

        [FieldOrder(3)]
        [FieldLength(24)]
        [SerializeAs(SerializedType.TerminatedString)]
        public string SoftwareVersion { get; set; } = About.VersionString;

        [FieldOrder(4)]
        [FieldLength(24)]
        [SerializeAs(SerializedType.TerminatedString)]
        public string FileCreateTime { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        [FieldOrder(5)]
        [FieldLength(32)]
        [SerializeAs(SerializedType.TerminatedString)]
        public string MachineName { get; set; } = DefaultMachineName;

        [FieldOrder(6)]
        [FieldLength(32)]
        [SerializeAs(SerializedType.TerminatedString)]
        public string MachineType { get; set; } = "DLP";

        [FieldOrder(7)]
        [FieldLength(32)]
        [SerializeAs(SerializedType.TerminatedString)]
        public string ProfileName { get; set; } = About.Software;

        [FieldOrder(8)] public ushort AntiAliasingLevel { get; set; } = 8;
        [FieldOrder(9)] public ushort GreyLevel { get; set; } = 1;
        [FieldOrder(10)] public ushort BlurLevel { get; set; } = 0;

        [FieldOrder(11)]
        [FieldCount(116 * 116 * 2)]
        public byte[] SmallPreview565 { get; set; } = [];

        [FieldOrder(12)] [FieldCount(2)] public byte[] SmallPreviewDelimiter { get; set; } = Delimiter;

        [FieldOrder(13)]
        [FieldCount(290 * 290 * 2)]
        public byte[] BigPreview565 { get; set; } = [];

        [FieldOrder(14)] [FieldCount(2)] public byte[] BigPreviewDelimiter { get; set; } = Delimiter;
        [FieldOrder(15)] public uint LayerCount { get; set; }
        [FieldOrder(16)] public ushort ResolutionX { get; set; }
        [FieldOrder(17)] public ushort ResolutionY { get; set; }
        [FieldOrder(18)] public bool MirrorX { get; set; }
        [FieldOrder(19)] public bool MirrorY { get; set; }
        [FieldOrder(20)] public float DisplayWidth { get; set; }
        [FieldOrder(21)] public float DisplayHeight { get; set; }
        [FieldOrder(22)] public float MachineZ { get; set; }
        [FieldOrder(23)] public float LayerHeight { get; set; }
        [FieldOrder(24)] public float ExposureTime { get; set; }
        [FieldOrder(25)] public DelayModes DelayMode { get; set; } = DelayModes.WaitTime;
        [FieldOrder(26)] public float LightOffDelay { get; set; }
        [FieldOrder(27)] public float BottomWaitTimeAfterCure { get; set; }
        [FieldOrder(28)] public float BottomWaitTimeAfterLift { get; set; }
        [FieldOrder(29)] public float BottomWaitTimeBeforeCure { get; set; }
        [FieldOrder(30)] public float WaitTimeAfterCure { get; set; }
        [FieldOrder(31)] public float WaitTimeAfterLift { get; set; }
        [FieldOrder(32)] public float WaitTimeBeforeCure { get; set; }
        [FieldOrder(33)] public float BottomExposureTime { get; set; }
        [FieldOrder(34)] public uint BottomLayerCount { get; set; }
        [FieldOrder(35)] public float BottomLiftHeight { get; set; }
        [FieldOrder(36)] public float BottomLiftSpeed { get; set; }
        [FieldOrder(37)] public float LiftHeight { get; set; }
        [FieldOrder(38)] public float LiftSpeed { get; set; }
        [FieldOrder(39)] public float BottomRetractHeight { get; set; }
        [FieldOrder(40)] public float BottomRetractSpeed { get; set; }
        [FieldOrder(41)] public float RetractHeight { get; set; }
        [FieldOrder(42)] public float RetractSpeed { get; set; }
        [FieldOrder(43)] public float BottomLiftHeight2 { get; set; }
        [FieldOrder(44)] public float BottomLiftSpeed2 { get; set; }
        [FieldOrder(45)] public float LiftHeight2 { get; set; }
        [FieldOrder(46)] public float LiftSpeed2 { get; set; }
        [FieldOrder(47)] public float BottomRetractHeight2 { get; set; }
        [FieldOrder(48)] public float BottomRetractSpeed2 { get; set; }
        [FieldOrder(49)] public float RetractHeight2 { get; set; }
        [FieldOrder(50)] public float RetractSpeed2 { get; set; }
        [FieldOrder(51)] public ushort BottomLightPWM { get; set; } = DefaultBottomLightPWM;
        [FieldOrder(52)] public ushort LightPWM { get; set; } = DefaultLightPWM;
        [FieldOrder(53)] public bool PerLayerSettings { get; set; }
        [FieldOrder(54)] public uint PrintTime { get; set; }
        [FieldOrder(55)] public float Volume { get; set; }
        [FieldOrder(56)] public float MaterialGrams { get; set; }
        [FieldOrder(57)] public float MaterialCost { get; set; }
        [FieldOrder(58)]
        [FieldLength(8)]
        [SerializeAs(SerializedType.TerminatedString)]
        public string PriceCurrencySymbol { get; set; } = "$";

        [FieldOrder(59)] public uint OffsetLayerContent { get; set; }
        [FieldOrder(60)] public byte GrayScaleLevel { get; set; } = 1;
        [FieldOrder(61)] public ushort TransitionLayerCount { get; set; }

        // --- V5 extension fields ---
        [FieldOrder(62)] public byte PartitionCount { get; set; } = 2;
        [FieldOrder(63)] public uint LayerDefTableAddress { get; set; }
        [FieldOrder(64)] public uint ImageDefTableAddress { get; set; }
        [FieldOrder(65)] public uint ResinDefTableAddress { get; set; }
        [FieldOrder(66)] public byte SupportAntiAliasing { get; set; }
        [FieldOrder(67)] public byte PixelBitWidth { get; set; } = 3;
        [FieldOrder(68)] public uint ExtDefTableAddress { get; set; }
    }

    public class LayerDef
    {
        [FieldOrder(0)] public ushort Pause { get; set; }
        [FieldOrder(1)] public float PausePositionZ { get; set; }
        [FieldOrder(2)] public float PositionZ { get; set; }
        [FieldOrder(3)] public float ExposureTime { get; set; }
        [FieldOrder(4)] public float LightOffDelay { get; set; }
        [FieldOrder(5)] public float WaitTimeAfterCure { get; set; }
        [FieldOrder(6)] public float WaitTimeAfterLift { get; set; }
        [FieldOrder(7)] public float WaitTimeBeforeCure { get; set; }
        [FieldOrder(8)] public float LiftHeight { get; set; }
        [FieldOrder(9)] public float LiftSpeed { get; set; }
        [FieldOrder(10)] public float LiftHeight2 { get; set; }
        [FieldOrder(11)] public float LiftSpeed2 { get; set; }
        [FieldOrder(12)] public float RetractHeight { get; set; }
        [FieldOrder(13)] public float RetractSpeed { get; set; }
        [FieldOrder(14)] public float RetractHeight2 { get; set; }
        [FieldOrder(15)] public float RetractSpeed2 { get; set; }
        [FieldOrder(16)] public ushort LightPWM { get; set; }
        [FieldOrder(17)] [FieldCount(2)] public byte[] DelimiterData { get; set; } = Delimiter;

        [Ignore] public GooV5File? Parent { get; set; }
        [Ignore] public byte[] EncodedRle { get; set; } = [];

        public LayerDef() { }

        public LayerDef(GooV5File parent, Layer layer)
        {
            Parent = parent;
            PausePositionZ = parent.MachineZ;
            SetFrom(layer);
        }

        public void SetFrom(Layer layer)
        {
            PositionZ = layer.PositionZ;
            ExposureTime = layer.ExposureTime;
            LightOffDelay = layer.LightOffDelay;
            LiftHeight = layer.LiftHeight;
            LiftSpeed = layer.LiftSpeed;
            LiftHeight2 = layer.LiftHeight2;
            LiftSpeed2 = layer.LiftSpeed2;
            RetractSpeed = layer.RetractSpeed;
            RetractHeight = layer.RetractHeight;
            RetractHeight2 = layer.RetractHeight2;
            RetractSpeed2 = layer.RetractSpeed2;
            WaitTimeAfterCure = layer.WaitTimeAfterCure;
            WaitTimeAfterLift = layer.WaitTimeAfterLift;
            WaitTimeBeforeCure = layer.WaitTimeBeforeCure;
            LightPWM = layer.LightPWM;
        }

        public void CopyTo(Layer layer)
        {
            layer.PositionZ = PositionZ;
            layer.ExposureTime = ExposureTime;
            layer.LightOffDelay = LightOffDelay;
            layer.LiftHeight = LiftHeight;
            layer.LiftSpeed = LiftSpeed;
            layer.LiftHeight2 = LiftHeight2;
           layer.LiftSpeed2 = LiftSpeed2;
           layer.RetractSpeed = RetractSpeed;
           layer.RetractHeight2 = RetractHeight2;
           layer.RetractSpeed2 = RetractSpeed2;
            layer.WaitTimeAfterCure = WaitTimeAfterCure;
            layer.WaitTimeAfterLift = WaitTimeAfterLift;
            layer.WaitTimeBeforeCure = WaitTimeBeforeCure;
            layer.LightPWM = (byte)LightPWM;
        }

        public Mat DecodeImagePartition(uint layerIndex, int partitionIndex, uint halfWidth, uint height)
        {
            var mat = EmguCvExtensions.InitMat(new Size((int)halfWidth, (int)height));
            if (EncodedRle.Length <= 3) return mat;

            if (EncodedRle[0] != LayerMagic)
                throw new MessageException(
                    $"RLE for layer {layerIndex} partition {partitionIndex} is corrupted");

            var lastByteIndex = EncodedRle.Length - 1;
            byte checkSum = 0;
            for (var i = 1; i < lastByteIndex; i++)
                unchecked { checkSum += EncodedRle[i]; }
            checkSum = (byte)~checkSum;
            if (EncodedRle[^1] != checkSum)
                throw new MessageException(
                    $"RLE checksum mismatch for layer {layerIndex} partition {partitionIndex}");

            var pixelBw = Parent?.PixelBitWidth ?? 3;
            VufCodec.DecodeInto(EncodedRle, 1, lastByteIndex, mat, (int)(halfWidth * height), pixelBw);
            return mat;
        }

        public byte[] EncodeImagePartition(Mat image, byte pixelBw)
        {
            var span = image.GetReadOnlySpanOfBytes();
            var encoded = VufCodec.Encode(span, pixelBw);

            var result = new byte[encoded.Length + 2];
            result[0] = LayerMagic;
            Buffer.BlockCopy(encoded, 0, result, 1, encoded.Length);

            byte checkSum = 0;
            for (var i = 1; i < result.Length - 1; i++)
                unchecked { checkSum += result[i]; }
            result[^1] = (byte)~checkSum;

            EncodedRle = result;
            return result;
        }
    }

   public struct IndexTableEntry
   {
       public int Offset;
       public int Size;
   }

   /// <summary>
   /// Resin definition block (V5.2 layout, 270 bytes).
   /// Layout per GOO V5 spec: magic(1) + name(128) + type(128) +
   /// color(3) + density(4) + stickiness(4) + delimiter(2).
   /// </summary>
   public class ResinDef
   {
       [FieldOrder(0)] public byte Magic { get; set; } = ResinDataMagic;

       [FieldOrder(1)]
       [FieldLength(128)]
       [SerializeAs(SerializedType.TerminatedString)]
       public string Name { get; set; } = FileFormat.DefaultResinName;

       [FieldOrder(2)]
       [FieldLength(128)]
       [SerializeAs(SerializedType.TerminatedString)]
       public string ResinType { get; set; } = string.Empty;

       [FieldOrder(3)] [FieldCount(3)] public byte[] Color { get; set; } = [0x80, 0x80, 0x80];
       [FieldOrder(4)] public float Density { get; set; } = 1.0f;
       [FieldOrder(5)] public float Stickiness { get; set; } = 0.5f;
       [FieldOrder(6)] [FieldCount(2)] public byte[] Delimiter { get; set; } = GooV5File.Delimiter;

       public ResinDef() { }

       public static ResinDef FromRaw(byte[] data)
       {
           if (data.Length < 270 || data[0] != ResinDataMagic) return new ResinDef();
           return Helpers.Deserialize<ResinDef>(new MemoryStream(data), Endianness.Little);
       }
   }

   #endregion

    #region VUF RLE Codec

    public static class VufCodec
    {
        public static void DecodeInto(byte[] data, int start, int end, Mat mat, int expectedPixels, int pixelBw)
        {
            var grayMax = (byte)((1 << pixelBw) - 1);
            // Pre-compute a lookup table: VUF [0, grayMax] -> 8-bit [0, 255]
            Span<byte> colorLut = stackalloc byte[256];
            if (grayMax == 0)
            {
                colorLut[0] = 0;
            }
            else
            {
                for (var v = 0; v <= grayMax; v++)
                    colorLut[v] = (byte)Math.Min(255, (v * 255 + grayMax / 2) / grayMax);
            }
            var pixel = 0;
            byte prevValue = 0;
            var i = start;

            while (i < end && pixel < expectedPixels)
            {
                var tag = data[i];
                var chunkType = (byte)(tag >> 6);

                if (chunkType == 0x01) // RUN
                {
                    var opt = (tag >> 4) & 0x03;
                    var count = tag & 0x0F;
                    var shift = 4;
                    for (var s = 0; s < opt && i + 1 < end; s++)
                    {
                        i++;
                        count |= data[i] << shift;
                        shift += 8;
                    }
                    count += 1;
                    var remaining = expectedPixels - pixel;
                    if (count > remaining) count = remaining;
                    mat.FillSpan(ref pixel, count, colorLut[prevValue]);
                    i++;
                }
              else if (chunkType == 0x02) // DIFF
              {
                  // Device uses 0xA0 as a special case for diff=+32, since
                  // 0x80|(32+32)=0xC0 would collide with the RUN prefix.
                  var diff = tag == 0xA0 ? 32 : (tag & 0x3F) - 32;
                  prevValue = (byte)(prevValue + diff);
                   if (pixel < expectedPixels)
                       mat.FillSpan(ref pixel, 1, colorLut[prevValue]);
                   i++;
               }
                else if (chunkType == 0x00) // GRAY
                {
                    var countBits = (tag >> 3) & 0x07;
                    byte value;
                    int count;
                    if (countBits != 0)
                    {
                        count = countBits;
                        value = (tag & 0x07) == 0x07 ? grayMax : (byte)(tag & 0x07);
                    }
                    else
                    {
                        if (i + 1 >= end) break;
                        count = (tag & 0x07) + 1;
                        i++;
                        value = data[i];
                    }
                    var remaining = expectedPixels - pixel;
                    if (count > remaining) count = remaining;
                    mat.FillSpan(ref pixel, count, colorLut[value]);
                    prevValue = value;
                    i++;
                }
                else // 0x03 — unused
                {
                    break;
                }
            }
        }

        public static byte[] Encode(ReadOnlySpan<byte> pixels, byte pixelBw)
        {
            var output = new List<byte>(pixels.Length / 4 + 16);
            if (pixels.IsEmpty || pixelBw == 0 || pixelBw > 8) return output.ToArray();

            var grayMax = (byte)((1 << pixelBw) - 1);
            // UVtools pixels are 8-bit [0,255]; quantize to [0, grayMax] for VUF encoding
            byte Quantize(byte v) => (byte)((v * grayMax + 127) / 255);
            byte prevChunkValue = 0;
            byte runValue = Quantize(pixels[0]);
            uint run = 0;

            for (int pos = 0; pos < pixels.Length; pos++)
            {
                var q = Quantize(pixels[pos]);
                if (q == runValue) { run++; continue; }
                EncodeChunk(output, run, runValue, prevChunkValue, pixelBw, grayMax);
                prevChunkValue = runValue;
                runValue = q;
                run = 1;
            }
            if (run > 0)
                EncodeChunk(output, run, runValue, prevChunkValue, pixelBw, grayMax);

            return output.ToArray();
        }

        private static void EncodeChunk(List<byte> output, uint run, byte value,
            byte prevValue, byte pixelBw, byte grayMax)
        {
            int diff = (int)value - (int)prevValue;

          if (diff == 0) { EncodeRunChunk(output, run); return; }
          if (run == 1 && diff >= -32 && diff <= 32) { EncodeDiffChunk(output, diff); return; }
          if (run <= 7 && pixelBw <= 3) { EncodeGreyChunk(output, run, value, grayMax); return; }
          if (diff >= -32 && diff <= 32 && run > 1)
          { EncodeDiffChunk(output, diff); EncodeRunChunk(output, run - 1); return; }
            if ((value == 0 || value == grayMax) && run <= 7)
            { EncodeGreyChunk(output, run, value, grayMax); return; }
            if ((value == 0 || value == grayMax) && run > 7)
            { EncodeGreyChunk(output, 7, value, grayMax); EncodeRunChunk(output, run - 7); return; }

            var head = run < 8 ? run : 8;
            EncodeGreyChunk(output, head, value, grayMax);
            if (run > head) EncodeRunChunk(output, run - head);
        }

        private static void EncodeRunChunk(List<byte> output, uint size)
        {
            while (size > 0)
            {
                uint enLen;
                if (size <= 0x10) { enLen = size; output.Add((byte)(VufRunOp | (enLen - 1))); }
                else if (size <= 0x1000)
                { enLen = size; output.Add((byte)(0x50 | ((enLen - 1) & 0xF))); output.Add((byte)(((enLen - 1) >> 4) & 0xFF)); }
                else if (size <= 0x100000)
                { enLen = size; output.Add((byte)(0x60 | ((enLen - 1) & 0xF))); output.Add((byte)(((enLen - 1) >> 4) & 0xFF)); output.Add((byte)(((enLen - 1) >> 12) & 0xFF)); }
                else
                { enLen = Math.Min(size, 0x10000000u); output.Add((byte)(0x70 | ((enLen - 1) & 0xF))); output.Add((byte)(((enLen - 1) >> 4) & 0xFF)); output.Add((byte)(((enLen - 1) >> 12) & 0xFF)); output.Add((byte)(((enLen - 1) >> 20) & 0xFF)); }
                size -= enLen;
            }
        }

      private static void EncodeDiffChunk(List<byte> output, int diff)
      {
              if (diff == 32)
              output.Add(0xA0); // normal encoding would collide with RUN prefix
          else
              output.Add((byte)(VufDiffOp | (diff + 32)));
      }

        private static void EncodeGreyChunk(List<byte> output, uint run, byte value, byte grayMax)
        {
            if (run > 8) run = 8;
            if (run <= 7 && value < 7)
                output.Add((byte)(VufGrayOp | (run << 3) | value));
            else if (run <= 7 && value == grayMax)
                output.Add((byte)(VufGrayOp | (run << 3) | 0x07));
            else
            { output.Add((byte)(VufGrayOp | (run - 1))); output.Add(value); }
        }
    }

    #endregion

    #region Properties

    public override FileFormatType FileType => FileFormatType.Binary;
    public override Endianness FileFormatEndianness => Endianness.Little;

    public override FileExtension[] FileExtensions { get; } =
    [
        new(typeof(GooV5File), "goo", "Elegoo GOO V5")
    ];

    public override Size[] ThumbnailsOriginalSize { get; } = [new(116, 116), new(290, 290)];

    public FileHeader Header { get; private set; } = new();
    public LayerDef[]? LayersDefinition { get; private set; }

    private IndexTableEntry[]? _ldtEntries;
    private IndexTableEntry[]? _iedtEntries;
   private IndexTableEntry[]? _rdtEntries;
   private byte[] _resinData = [];

   /// <summary>Parsed resin definition; always present after decode.</summary>
   public ResinDef Resin { get; private set; } = new();

   // Extension Definition Table (EDT) - V5.2+ files may include one.
   // We store each extension block individually so entry offsets can be
   // rewritten when the file layout changes during re-encode.
   private List<byte[]> _edtBlocks = [];
   private uint _edtOffset;  // 0 = no EDT

    public byte PixelBitWidth
    {
        get => Header.PixelBitWidth == 0 ? (byte)3 : Header.PixelBitWidth;
        set => Header.PixelBitWidth = value;
    }

    public byte PartitionCount
    {
        get => Header.PartitionCount == 0 ? (byte)2 : Header.PartitionCount;
        set => Header.PartitionCount = value;
    }

    public override PrintParameterModifier[] PrintParameterModifiers => HaveTiltingVat ? [
        PrintParameterModifier.BottomLayerCount, PrintParameterModifier.TransitionLayerCount,
        PrintParameterModifier.LightOffDelay,
        PrintParameterModifier.BottomWaitTimeBeforeCure, PrintParameterModifier.WaitTimeBeforeCure,
        PrintParameterModifier.BottomExposureTime, PrintParameterModifier.ExposureTime,
        PrintParameterModifier.BottomWaitTimeAfterCure, PrintParameterModifier.WaitTimeAfterCure,
        PrintParameterModifier.BottomWaitTimeAfterLift, PrintParameterModifier.WaitTimeAfterLift,
        PrintParameterModifier.BottomLightPWM, PrintParameterModifier.LightPWM
    ] : [
        PrintParameterModifier.BottomLayerCount, PrintParameterModifier.TransitionLayerCount,
        PrintParameterModifier.LightOffDelay,
        PrintParameterModifier.BottomWaitTimeBeforeCure, PrintParameterModifier.WaitTimeBeforeCure,
        PrintParameterModifier.BottomExposureTime, PrintParameterModifier.ExposureTime,
        PrintParameterModifier.BottomWaitTimeAfterCure, PrintParameterModifier.WaitTimeAfterCure,
        PrintParameterModifier.BottomLiftHeight, PrintParameterModifier.BottomLiftSpeed,
        PrintParameterModifier.LiftHeight, PrintParameterModifier.LiftSpeed,
        PrintParameterModifier.BottomLiftHeight2, PrintParameterModifier.BottomLiftSpeed2,
        PrintParameterModifier.LiftHeight2, PrintParameterModifier.LiftSpeed2,
        PrintParameterModifier.BottomWaitTimeAfterLift, PrintParameterModifier.WaitTimeAfterLift,
        PrintParameterModifier.BottomRetractSpeed, PrintParameterModifier.RetractSpeed,
        PrintParameterModifier.BottomRetractHeight2, PrintParameterModifier.BottomRetractSpeed2,
        PrintParameterModifier.RetractHeight2, PrintParameterModifier.RetractSpeed2,
        PrintParameterModifier.BottomLightPWM, PrintParameterModifier.LightPWM
    ];

    public override PrintParameterModifier[] PrintParameterPerLayerModifiers => !IsPerLayerSettingsAllowed
        ? base.PrintParameterPerLayerModifiers
        : HaveTiltingVat ? [
            PrintParameterModifier.PositionZ, PrintParameterModifier.LightOffDelay,
            PrintParameterModifier.WaitTimeBeforeCure, PrintParameterModifier.ExposureTime,
            PrintParameterModifier.WaitTimeAfterCure, PrintParameterModifier.WaitTimeAfterLift,
            PrintParameterModifier.LightPWM
        ] : [
            PrintParameterModifier.PositionZ, PrintParameterModifier.LightOffDelay,
            PrintParameterModifier.WaitTimeBeforeCure, PrintParameterModifier.ExposureTime,
            PrintParameterModifier.WaitTimeAfterCure,
            PrintParameterModifier.LiftHeight, PrintParameterModifier.LiftSpeed,
            PrintParameterModifier.LiftHeight2, PrintParameterModifier.LiftSpeed2,
            PrintParameterModifier.WaitTimeAfterLift,
            PrintParameterModifier.RetractSpeed, PrintParameterModifier.RetractHeight2,
            PrintParameterModifier.RetractSpeed2, PrintParameterModifier.LightPWM
        ];

    public override bool HaveTiltingVat => MachineName.Contains("Saturn 4 Ultra", StringComparison.OrdinalIgnoreCase)
        || MachineName.Contains("Mars 5 Ultra", StringComparison.OrdinalIgnoreCase)
        || LiftHeight == 0;

    public override uint[] AvailableVersions { get; } = [50, 51, 52];

    public override uint Version
    {
        get => Header.Version.Length >= 4 && Header.Version[0] == 'V'
            ? (uint)((Header.Version[1] - '0') * 10 + Header.Version[3] - '0') : 51;
        set { base.Version = value; Header.Version = base.Version < 10 ? $"V{base.Version}.0" : $"V{base.Version / 10}.{base.Version % 10}"; }
    }

    public override bool CanProcess(string? fileFullPath)
    {
        if (!base.CanProcess(fileFullPath)) return false;
        if (string.IsNullOrWhiteSpace(fileFullPath)) return false;
        try
        {
            using var stream = File.OpenRead(fileFullPath);
            Span<byte> hdr = stackalloc byte[12];
            if (stream.Read(hdr) != 12) return false;
            if (hdr[0] != (byte)'V' || hdr[1] != (byte)'5') return false;
            for (var i = 0; i < 8; i++) if (hdr[4 + i] != FileMagic[i]) return false;
            return true;
        }
        catch { return false; }
    }

    public override uint ResolutionX { get => Header.ResolutionX; set => base.ResolutionX = Header.ResolutionX = (ushort)value; }
    public override uint ResolutionY { get => Header.ResolutionY; set => base.ResolutionY = Header.ResolutionY = (ushort)value; }
    public override float LayerHeight { get => Header.LayerHeight; set => base.LayerHeight = Header.LayerHeight = value; }
    public override float DisplayWidth { get => Header.DisplayWidth; set => base.DisplayWidth = Header.DisplayWidth = RoundDisplaySize(value); }
    public override float DisplayHeight { get => Header.DisplayHeight; set => base.DisplayHeight = Header.DisplayHeight = RoundDisplaySize(value); }
    public override float MachineZ { get => Header.MachineZ > 0 ? Header.MachineZ : base.MachineZ; set => base.MachineZ = Header.MachineZ = MathF.Round(value, 2); }

    public override FlipDirection DisplayMirror
    {
        get => Header is { MirrorX: true, MirrorY: true } ? FlipDirection.Both
            : Header.MirrorX ? FlipDirection.Horizontally : Header.MirrorY ? FlipDirection.Vertically : FlipDirection.None;
        set { Header.MirrorX = value is FlipDirection.Horizontally or FlipDirection.Both; Header.MirrorY = value is FlipDirection.Vertically or FlipDirection.Both; OnPropertyChanged(); }
    }

    public override byte AntiAliasing { get => (byte)Header.AntiAliasingLevel; set => base.AntiAliasing = (byte)(Header.AntiAliasingLevel = value); }
    public override uint LayerCount { get => base.LayerCount; set => base.LayerCount = Header.LayerCount = base.LayerCount; }
    public override ushort BottomLayerCount { get => (ushort)Header.BottomLayerCount; set => base.BottomLayerCount = (ushort)(Header.BottomLayerCount = value); }
    public override TransitionLayerTypes TransitionLayerType => TransitionLayerTypes.Software;
    public override ushort TransitionLayerCount { get => Header.TransitionLayerCount; set => base.TransitionLayerCount = Header.TransitionLayerCount = (ushort)Math.Min(value, MaximumPossibleTransitionLayerCount); }
    public override float BottomLightOffDelay => Header.LightOffDelay;

    public override float LightOffDelay { get => Header.LightOffDelay; set { base.LightOffDelay = Header.LightOffDelay = MathF.Round(value, 2); if (value > 0) Header.DelayMode = DelayModes.LightOff; } }
    public override float BottomWaitTimeBeforeCure { get => Header.BottomWaitTimeBeforeCure; set { base.BottomWaitTimeBeforeCure = Header.BottomWaitTimeBeforeCure = MathF.Round(value, 2); Header.DelayMode = DelayModes.WaitTime; } }
    public override float WaitTimeBeforeCure { get => Header.WaitTimeBeforeCure; set { base.WaitTimeBeforeCure = Header.WaitTimeBeforeCure = MathF.Round(value, 2); if (value > 0) { BottomLightOffDelay = 0; LightOffDelay = 0; } Header.DelayMode = DelayModes.WaitTime; } }
    public override float BottomExposureTime { get => Header.BottomExposureTime; set => base.BottomExposureTime = Header.BottomExposureTime = MathF.Round(value, 2); }
    public override float BottomWaitTimeAfterCure { get => Header.BottomWaitTimeAfterCure; set { base.BottomWaitTimeAfterCure = Header.BottomWaitTimeAfterCure = MathF.Round(value, 2); Header.DelayMode = DelayModes.WaitTime; } }
    public override float WaitTimeAfterCure { get => Header.WaitTimeAfterCure; set { base.WaitTimeAfterCure = Header.WaitTimeAfterCure = MathF.Round(value, 2); if (value > 0) { BottomLightOffDelay = 0; LightOffDelay = 0; } Header.DelayMode = DelayModes.WaitTime; } }
    public override float ExposureTime { get => Header.ExposureTime; set => base.ExposureTime = Header.ExposureTime = MathF.Round(value, 2); }
    public override float BottomLiftHeight { get => Header.BottomLiftHeight; set => base.BottomLiftHeight = Header.BottomLiftHeight = MathF.Round(value, 2); }
    public override float BottomLiftSpeed { get => Header.BottomLiftSpeed; set => base.BottomLiftSpeed = Header.BottomLiftSpeed = MathF.Round(value, 2); }
    public override float LiftHeight { get => Header.LiftHeight; set => base.LiftHeight = Header.LiftHeight = MathF.Round(value, 2); }
    public override float LiftSpeed { get => Header.LiftSpeed; set => base.LiftSpeed = Header.LiftSpeed = MathF.Round(value, 2); }
    public override float BottomLiftHeight2 { get => Header.BottomLiftHeight2; set => base.BottomLiftHeight2 = Header.BottomLiftHeight2 = MathF.Round(value, 2); }
    public override float BottomLiftSpeed2 { get => Header.BottomLiftSpeed2; set => base.BottomLiftSpeed2 = Header.BottomLiftSpeed2 = MathF.Round(value, 2); }
    public override float LiftHeight2 { get => Header.LiftHeight2; set => base.LiftHeight2 = Header.LiftHeight2 = MathF.Round(value, 2); }
    public override float LiftSpeed2 { get => Header.LiftSpeed2; set => base.LiftSpeed2 = Header.LiftSpeed2 = MathF.Round(value, 2); }
    public override float BottomWaitTimeAfterLift { get => Header.BottomWaitTimeAfterLift; set { base.BottomWaitTimeAfterLift = Header.BottomWaitTimeAfterLift = MathF.Round(value, 2); Header.DelayMode = DelayModes.WaitTime; } }
    public override float WaitTimeAfterLift { get => Header.WaitTimeAfterLift; set { base.WaitTimeAfterLift = Header.WaitTimeAfterLift = MathF.Round(value, 2); if (value > 0) { BottomLightOffDelay = 0; LightOffDelay = 0; } Header.DelayMode = DelayModes.WaitTime; } }
    public override float BottomRetractSpeed { get => Header.BottomRetractSpeed; set => base.BottomRetractSpeed = Header.BottomRetractSpeed = MathF.Round(value, 2); }
    public override float RetractSpeed { get => Header.RetractSpeed; set => base.RetractSpeed = Header.RetractSpeed = MathF.Round(value, 2); }
    public override float BottomRetractHeight2 { get => Header.BottomRetractHeight2; set { value = Math.Clamp(MathF.Round(value, 2), 0, BottomRetractHeightTotal); base.BottomRetractHeight2 = Header.BottomRetractHeight2 = value; Header.BottomRetractHeight = BottomRetractHeight; } }
    public override float BottomRetractSpeed2 { get => Header.BottomRetractSpeed2; set => base.BottomRetractSpeed2 = Header.BottomRetractSpeed2 = MathF.Round(value, 2); }
    public override float RetractHeight2 { get => Header.RetractHeight2; set { value = Math.Clamp(MathF.Round(value, 2), 0, RetractHeightTotal); base.RetractHeight2 = Header.RetractHeight2 = value; Header.RetractHeight = RetractHeight; } }
    public override float RetractSpeed2 { get => Header.RetractSpeed2; set => base.RetractSpeed2 = Header.RetractSpeed2 = MathF.Round(value, 2); }
    public override byte BottomLightPWM { get => (byte)Header.BottomLightPWM; set => base.BottomLightPWM = (byte)(Header.BottomLightPWM = value); }
    public override byte LightPWM { get => (byte)Header.LightPWM; set => base.LightPWM = (byte)(Header.LightPWM = value); }
    public override float PrintTime { get => base.PrintTime; set { base.PrintTime = value; Header.PrintTime = (uint)base.PrintTime; } }
    public override string MachineName { get => Header.MachineName; set => base.MachineName = Header.MachineName = value; }
    public override float MaterialGrams { get => Header.MaterialGrams; set => base.MaterialGrams = Header.MaterialGrams = MathF.Round(value, 3); }
    public override float MaterialCost { get => MathF.Round(Header.MaterialCost, 3); set => base.MaterialCost = Header.MaterialCost = MathF.Round(value, 3); }

    public override object[] Configs => [Header];

    #endregion

    #region Constructors
    public GooV5File() { }
    #endregion

    #region Methods

    protected override void DecodeInternally(OperationProgress progress)
    {
        using var inputFile = new FileStream(FileFullPath!, FileMode.Open, FileAccess.Read);
        Header = Helpers.Deserialize<FileHeader>(inputFile, Endianness.Little);
        ThrowIfVersionOutOfRange();

        if (!Header.Magic.AsValueEnumerable().SequenceEqual(FileMagic))
            throw new FileLoadException("Not a valid GOO V5 file! Magic value mismatch", FileFullPath);

        progress.Reset(OperationProgress.StatusDecodePreviews, (uint)ThumbnailCountFileShouldHave);
        Thumbnails.Add(DecodeImage(DATATYPE_RGB565, Header.SmallPreview565, ThumbnailsOriginalSize[0]));
        progress++;
        Thumbnails.Add(DecodeImage(DATATYPE_RGB565, Header.BigPreview565, ThumbnailsOriginalSize[1]));
        progress++;

        var partitions = PartitionCount;
        var halfWidth = ResolutionX / partitions;

        progress.Reset(OperationProgress.StatusDecodeLayers, Header.LayerCount);
        Init(Header.LayerCount, DecodeType == FileDecodeType.Partial);
        LayersDefinition = new LayerDef[LayerCount];

        _ldtEntries = ReadIndexTable(inputFile, ResolveTableOffset(inputFile, Header.LayerDefTableAddress, LdtMagic), LdtMagic, (int)LayerCount);
        var imageCount = LayerCount * partitions;
        _iedtEntries = ReadIndexTable(inputFile, ResolveTableOffset(inputFile, Header.ImageDefTableAddress, IedtMagic), IedtMagic, (int)imageCount);
       _rdtEntries = ReadResinTable(inputFile, ResolveTableOffset(inputFile, Header.ResinDefTableAddress, RdtMagic, counted: true));

       ResolveExtensionFields(inputFile);
        // Re-read partitions after potential fallback
        partitions = PartitionCount;

        // Two-phase decode per batch (same pattern as GooFile V3):
        // Phase 1 — serial I/O: read layer definitions + raw RLE blocks into memory
        // Phase 2 — parallel: decode images, merge partitions, create layers
        foreach (var batch in BatchLayersIndexes())
        {
            // Phase 1: serial reads from the file stream
            var batchRleBlocks = new List<byte[][]>(); // [layerInBatch][partition] -> raw RLE
            var batchLayerIndexes = System.Linq.Enumerable.ToArray(batch);

            for (var bi = 0; bi < batchLayerIndexes.Length; bi++)
            {
                var layerIndex = batchLayerIndexes[bi];
                progress.PauseOrCancelIfRequested();

                var ldtEntry = _ldtEntries[layerIndex];
                inputFile.Seek(ldtEntry.Offset, SeekOrigin.Begin);
                LayersDefinition[layerIndex] = Helpers.Deserialize<LayerDef>(inputFile, Endianness.Little);
                LayersDefinition[layerIndex].Parent = this;

                var rleBlocks = new byte[partitions][];
                if (DecodeType == FileDecodeType.Full)
                {
                    for (byte p = 0; p < partitions; p++)
                    {
                        var imgIndex = layerIndex * partitions + p;
                        if (imgIndex >= _iedtEntries.Length) break;
                        var iedtEntry = _iedtEntries[imgIndex];
                        inputFile.Seek(iedtEntry.Offset, SeekOrigin.Begin);
                        var rawBlock = inputFile.ReadBytes(iedtEntry.Size);
                        if (rawBlock.Length > 2 && rawBlock[^2] == 0x0D && rawBlock[^1] == 0x0A)
                            rawBlock = rawBlock[..^2];
                        rleBlocks[p] = rawBlock;
                    }
                }
                batchRleBlocks.Add(rleBlocks);
            }

            // Phase 2: parallel decode + merge
            if (DecodeType == FileDecodeType.Full)
            {
                Parallel.For(0, batchLayerIndexes.Length, CoreSettings.GetParallelOptions(progress), bi =>
                {
                    progress.PauseIfRequested();

                    var layerIndex = batchLayerIndexes[bi];
                    var rleBlocks = batchRleBlocks[bi];
                    var mats = new Mat[partitions];

                    for (byte p = 0; p < partitions; p++)
                    {
                        if (rleBlocks[p] is null) break;
                        LayersDefinition![layerIndex].EncodedRle = rleBlocks[p];
                        mats[p] = LayersDefinition[layerIndex].DecodeImagePartition(
                            (uint)layerIndex, p, halfWidth, ResolutionY);
                    }

                    var fullMat = MergePartitions(mats, halfWidth, partitions);
                    _layers[layerIndex] = new Layer((uint)layerIndex, fullMat, this);

                    progress.LockAndIncrement();
                });
            }
            else
            {
                for (var i = 0; i < batchLayerIndexes.Length; i++)
                    progress.LockAndIncrement();
            }
        }

       if (_rdtEntries is { Length: > 0 })
       {
           var resinEntry = _rdtEntries[0];
           var resinOffset = resinEntry.Offset;
           int resinLength;
           // RDT entry size includes the trailing CRLF delimiter
           if (resinEntry.Size > 2 && (long)resinOffset + resinEntry.Size <= inputFile.Length)
               resinLength = resinEntry.Size - 2;
           else
               resinLength = (int)inputFile.Length - 34 - resinOffset; // fallback: read to CRLF+MD5
      if (resinOffset > 0 && resinLength > 0)
      {
          inputFile.Seek(resinOffset, SeekOrigin.Begin);
          _resinData = inputFile.ReadBytes(resinLength);
          // Parse the resin definition block (starts with 0x66 magic)
          if (_resinData.Length >= 270 && _resinData[0] == ResinDataMagic)
              Resin = ResinDef.FromRaw(_resinData);
      }
  }

        // Read EDT (Extension Definition Table) — V5.2+ may have one.
        // The EDT offset is stored at a fixed file position right after
        // the header struct (offset 195492), validated by 0xA4 magic.
        ReadEdtData(inputFile);

        for (uint layerIndex = 0; layerIndex < LayerCount; layerIndex++)
            LayersDefinition[layerIndex].CopyTo(this[layerIndex]);

        SuppressRebuildPropertiesWork(() =>
        {
            var enumerable = this.AsValueEnumerable();
            base.BottomWaitTimeBeforeCure = enumerable.FirstOrDefault(l => l is { IsBottomLayer: true, IsDummy: false })?.WaitTimeBeforeCure ?? 0;
            base.BottomWaitTimeAfterCure = enumerable.FirstOrDefault(l => l is { IsBottomLayer: true, IsDummy: false })?.WaitTimeAfterCure ?? 0;
            base.BottomWaitTimeAfterLift = enumerable.FirstOrDefault(l => l is { IsBottomLayer: true, IsDummy: false })?.WaitTimeAfterLift ?? 0;
        });
    }

    /// <summary>
    /// Validate a table offset from the header; if it doesn't point to the
    /// expected magic byte, scan the file for the best candidate. This makes
    /// decoding robust against new header layouts where the extension fields
    /// may be at different offsets or absent entirely.
   /// </summary>
   private static uint ResolveTableOffset(FileStream fs, uint headerOffset, byte magic, bool counted = false)
   {
       // Fast path: header offset is valid and points to the right magic
       if (headerOffset > 0 && headerOffset < fs.Length)
       {
           fs.Position = headerOffset;
           if (fs.ReadByte() == magic) return headerOffset;
       }

       // Fallback: scan the file for the magic byte, then score each
       // candidate by how many consecutive valid (offset, size) entries
       // follow it. Pick the best-scoring position.
       var bestOffset = 0u;
       var bestScore = -1;

       fs.Seek(0, SeekOrigin.Begin);
       int b;
       while ((b = fs.ReadByte()) != -1)
       {
           if (b != magic) continue;
           var candidate = (uint)(fs.Position - 1);
           var score = ScoreTableAt(fs, candidate, magic, 8, counted);
           if (score > bestScore)
           {
               bestScore = score;
               bestOffset = candidate;
           }
       }

       return bestScore >= 1 ? bestOffset : 0u;
   }

    /// <summary>
    /// Count how many consecutive valid (offset, size) entries follow a
    /// magic byte at the given position. Returns -1 if the magic doesn't match.
    /// </summary>
   private static int ScoreTableAt(FileStream fs, uint pos, byte magic, int maxCheck = 8, bool counted = false)
   {
       if (pos >= fs.Length) return -1;
       fs.Position = pos;
       if (fs.ReadByte() != magic) return -1;

       var score = 0;
       var p = fs.Position;
       // Counted tables (RDT, EDT): magic(1) + count(u32) + entries
       if (counted)
       {
           if (p + 4 > fs.Length) return -1;
           fs.Position = p;
           var count = (int)fs.ReadUIntLittleEndian();
           p += 4;
           if (count <= 0 || count > 1024) return -1;
           maxCheck = Math.Min(maxCheck, count);
       }
       for (var i = 0; i < maxCheck; i++)
       {
           if (p + 8 > fs.Length) break;
           fs.Position = p;
           var offset = (int)fs.ReadUIntLittleEndian();
           var size = (int)fs.ReadUIntLittleEndian();

           if (offset == 0 && size == 0) break;
           if (offset <= 0 || size <= 0 || offset >= fs.Length) break;
           if ((long)offset + size > fs.Length) break;

           score++;
           p += 8;
       }

       return score;
   }

   /// <summary>
   /// Read the EDT (Extension Definition Table) if present.  The EDT offset
   /// is stored in the header (ExtDefTableAddress).  The EDT is a counted
   /// table: magic(1) + count(u32) + count × (offset, size).  Each entry
   /// points to an extension block; we store the raw blocks so they can be
   /// re-emitted with corrected offsets on encode.
   /// </summary>
   private void ReadEdtData(FileStream fs)
   {
       _edtOffset = 0;
       _edtBlocks = [];

       var rawOffset = (int)Header.ExtDefTableAddress;
       if (rawOffset <= 0 || rawOffset >= fs.Length) return;
       fs.Seek(rawOffset, SeekOrigin.Begin);
       if (fs.ReadByte() != EdtMagic) return;

      _edtOffset = (uint)rawOffset;

      // Read count then each entry's extension block
      var count = (int)fs.ReadUIntLittleEndian();
      if (count < 0 || count > 1024) return;

      for (var i = 0; i < count; i++)
      {
          if (fs.Position + 8 > fs.Length) break;
          var entryOffset = (int)fs.ReadUIntLittleEndian();
          var entrySize = (int)fs.ReadUIntLittleEndian();
          if (entryOffset <= 0 || entrySize <= 0) break;
          if ((long)entryOffset + entrySize > fs.Length) break;

          fs.Seek(entryOffset, SeekOrigin.Begin);
          var block = fs.ReadBytes(entrySize);
          // Anonymize NFC extension blocks (0x54): replace timing fields
          // (wash/uv/air-dry) with safe defaults so the device accepts them,
          // keep magic byte and CRLF delimiter intact.
          if (block.Length >= 15 && block[0] == 0x54)
          {
              // wash_time=1000, uv_expose_time=1000, air_dry_time=1000
              block[1] = 0xE8; block[2] = 0x03; block[3] = 0x00; block[4] = 0x00;
              block[5] = 0xE8; block[6] = 0x03; block[7] = 0x00; block[8] = 0x00;
              block[9] = 0xE8; block[10] = 0x03; block[11] = 0x00; block[12] = 0x00;
          }
          _edtBlocks.Add(block);
      }
  }

    /// <summary>
    /// Resolve V5 extension fields with fallback. If the deserialized header
    /// has invalid extension values (e.g. a newer format with a different
    /// layout), infer them from the file content.
    /// </summary>
   private void ResolveExtensionFields(FileStream fs)
   {
       // Fallback for PartitionCount: if 0 or implausible, infer from IEDT/LDT ratio
       if (PartitionCount == 0 || PartitionCount > 64)
       {
           if (_ldtEntries is { Length: > 0 } && _iedtEntries is { Length: > 0 })
           {
               var inferred = (byte)(_iedtEntries.Length / _ldtEntries.Length);
               if (inferred >= 1 && inferred <= 64) PartitionCount = inferred;
           }
           if (PartitionCount == 0 || PartitionCount > 64) PartitionCount = 2;
       }

        // Fallback for PixelBitWidth: if 0 or > 8, use most common default
        if (PixelBitWidth == 0 || PixelBitWidth > 8) PixelBitWidth = 3;
    }

    private IndexTableEntry[] ReadIndexTable(FileStream fs, uint tableAddress, byte magic, int maxEntries)
    {
        if (tableAddress == 0 || tableAddress >= fs.Length) return [];
        fs.Seek(tableAddress, SeekOrigin.Begin);
        if (fs.ReadByte() != magic) return [];

        var entries = new List<IndexTableEntry>();
        var i = 0;
        while (i < maxEntries && fs.Position + 8 <= fs.Length)
        {
            var offset = (int)fs.ReadUIntLittleEndian();
            var size = (int)fs.ReadUIntLittleEndian();
            if (offset == 0 && size == 0) break;
            if (offset < 0 || size <= 0 || offset >= fs.Length) break;
            if ((long)offset + size > fs.Length) break;
            entries.Add(new IndexTableEntry { Offset = offset, Size = size });
            i++;
        }
        return entries.ToArray();
    }

   private IndexTableEntry[] ReadResinTable(FileStream fs, uint tableAddress)
   {
       if (tableAddress == 0 || tableAddress >= fs.Length) return [];
       fs.Seek(tableAddress, SeekOrigin.Begin);
       if (fs.ReadByte() != RdtMagic) return [];

       // RDT is a counted table: magic(1) + count(u32) + count × (offset, size)
       var count = (int)fs.ReadUIntLittleEndian();
       if (count < 0 || count > 1024) return [];

       var entries = new List<IndexTableEntry>(count);
       for (var i = 0; i < count; i++)
       {
           if (fs.Position + 8 > fs.Length) break;
           var offset = (int)fs.ReadUIntLittleEndian();
           var size = (int)fs.ReadUIntLittleEndian();
           if (offset <= 0 || size <= 0 || offset >= fs.Length) break;
           if ((long)offset + size > fs.Length) break;
           entries.Add(new IndexTableEntry { Offset = offset, Size = size });
       }
       return entries.ToArray();
   }

    private static Mat MergePartitions(Mat[] partitions, uint halfWidth, byte partitionCount)
    {
        if (partitionCount == 1 && partitions[0] is not null) return partitions[0];
        var height = partitions[0]?.Height ?? 0;
        var fullWidth = (int)(halfWidth * partitionCount);
        var result = EmguCvExtensions.InitMat(new Size(fullWidth, height));
        for (byte p = 0; p < partitionCount; p++)
        {
            if (partitions[p] is null) continue;
            using var roi = result.Roi(new Rectangle(p * (int)halfWidth, 0, (int)halfWidth, height));
            partitions[p].CopyTo(roi);
            if (p > 0) partitions[p].Dispose();
        }
        return result;
    }

    private static Mat[] SplitPartitions(Mat full, uint halfWidth, byte partitionCount)
    {
        var partitions = new Mat[partitionCount];
        var fullWidth = full.Width;
        var height = full.Height;
        for (byte p = 0; p < partitionCount; p++)
        {
            partitions[p] = EmguCvExtensions.InitMat(new Size((int)halfWidth, height));
            using var fullRoi = full.Roi(new Rectangle(p * (int)halfWidth, 0, (int)halfWidth, height));
            fullRoi.CopyTo(partitions[p]);
        }
        return partitions;
    }

   protected override void OnBeforeEncode(bool isPartialEncode)
   {
       // advance_mode=0: device uses header-level settings for all layers.
      // Per-layer data is still written but the device ignores it for timing.
      Header.PerLayerSettings = false;
       Header.Volume = Volume;
       Header.MaterialGrams = MaterialMilliliters;
      Header.PartitionCount = PartitionCount;
      Header.PixelBitWidth = PixelBitWidth;

       // Ensure the resin definition block carries current material info
       // and is always serialized so the printer finds the 0x66 magic tag.
       if (Resin is null) Resin = new ResinDef();
      if (!string.IsNullOrWhiteSpace(MaterialName))
          Resin.Name = MaterialName;
      // Derive density from grams/ml if available, else default to water (1.0)
      Resin.Density = (MaterialMilliliters > 0 && MaterialGrams > 0)
          ? MaterialGrams / MaterialMilliliters : 1.0f;
      _resinData = Helpers.Serialize(Resin, Endianness.Little).ToArray();

      if (HaveTiltingVat)
       {
           const float lift = 0.05f;
            const float speed = lift;
            BottomLiftHeight = lift; BottomLiftSpeed = speed;
            BottomLiftHeight2 = 0; BottomLiftSpeed2 = 0;
            BottomRetractHeight2 = 0; BottomRetractSpeed2 = 0;
            BottomRetractSpeed = speed;
            LiftHeight = lift; LiftSpeed = speed;
            LiftHeight2 = 0; LiftSpeed2 = 0;
            RetractHeight2 = 0; RetractSpeed2 = 0;
            RetractSpeed = speed;
        }
    }

    protected override void EncodeInternally(OperationProgress progress)
    {
        var endianness = Endianness.Little;
        var partitions = PartitionCount;
        var halfWidth = ResolutionX / partitions;
        var pixelBw = PixelBitWidth;

        progress.Reset(OperationProgress.StatusEncodePreviews, 2);
        Mat?[] thumbnails = [GetLargestThumbnail(), GetSmallestThumbnail()];
       Header.BigPreview565 = EncodeImage(DATATYPE_RGB565, thumbnails[0]!);
       progress++;
       Header.SmallPreview565 = EncodeImage(DATATYPE_RGB565, thumbnails[1]!);
       progress++;

       var headerSize = (int)Helpers.Serializer.SizeOf(Header);
       var nLayers = (int)LayerCount;
       var nImages = nLayers * partitions;

       var ldtOffset = headerSize;
       var iedtOffset = ldtOffset + 1 + nLayers * 8;
       var rdtOffset = iedtOffset + 1 + nImages * 8;
       // RDT: magic(1) + count(4) + entry(8) when resin, else magic(1) + count(4)
       var rdtSize = 1 + 4 + (_resinData.Length > 0 ? 8 : 0);
       // Layer content starts right after RDT (no EDT table in output).
       var lcStart = rdtOffset + rdtSize;

       progress.Reset(OperationProgress.StatusEncodeLayers, LayerCount);
       var layerData = new LayerDef[LayerCount];
        var imageBlocks = new byte[nImages][];

        foreach (var batch in BatchLayersIndexes())
        {
            Parallel.ForEach(batch, CoreSettings.GetParallelOptions(progress), layerIndex =>
            {
                progress.PauseIfRequested();
                layerData[layerIndex] = new LayerDef(this, this[layerIndex]);
                using var fullMat = this[layerIndex].LayerMat;
                var parts = SplitPartitions(fullMat, halfWidth, partitions);
                for (byte p = 0; p < partitions; p++)
                {
                    var imgIndex = layerIndex * partitions + p;
                    imageBlocks[imgIndex] = layerData[layerIndex].EncodeImagePartition(parts[p], pixelBw);
                    parts[p].Dispose();
                }
                progress.LockAndIncrement();
            });
        }

        var contentOffset = lcStart;
        var layerBlockOffsets = new int[nLayers];
        for (var i = 0; i < nLayers; i++)
        { layerBlockOffsets[i] = contentOffset; contentOffset += (int)Helpers.Serializer.SizeOf(layerData[i]); }

       var imageBlockOffsets = new int[nImages];
       for (var i = 0; i < nImages; i++)
       { imageBlockOffsets[i] = contentOffset; contentOffset += imageBlocks[i].Length + 2; }

       var resinStart = contentOffset;
       // Resin data already includes trailing CRLF (ResinDef.Delimiter field).
       contentOffset += _resinData.Length;

       Header.LayerDefTableAddress = (uint)ldtOffset;
       Header.ImageDefTableAddress = (uint)iedtOffset;
       Header.ResinDefTableAddress = (uint)rdtOffset;
       Header.ExtDefTableAddress = 0; // no EDT in output
       // offset_layer_content points to the partition_count field in the header
       // (19 bytes before header end: partition_count + 3 table addrs +
       // support_aa + pixel_bitwidth + ext_table_addr)
       Header.OffsetLayerContent = (uint)(headerSize - 19);

       using var outputFile = new FileStream(TemporaryOutputFileFullPath, FileMode.Create, FileAccess.ReadWrite);

       outputFile.WriteSerialize(Header, endianness);

       outputFile.WriteByte(LdtMagic);
       for (var i = 0; i < nLayers; i++)
       { outputFile.WriteUIntLittleEndian((uint)layerBlockOffsets[i]); outputFile.WriteUIntLittleEndian((uint)Helpers.Serializer.SizeOf(layerData[i])); }

       outputFile.WriteByte(IedtMagic);
       for (var i = 0; i < nImages; i++)
       { outputFile.WriteUIntLittleEndian((uint)imageBlockOffsets[i]); outputFile.WriteUIntLittleEndian((uint)(imageBlocks[i].Length + 2)); }

       outputFile.WriteByte(RdtMagic);
       if (_resinData.Length > 0)
       {
           outputFile.WriteUIntLittleEndian(1); // count
           outputFile.WriteUIntLittleEndian((uint)resinStart); // entry offset
           outputFile.WriteUIntLittleEndian((uint)_resinData.Length); // entry size (incl CRLF)
       }
       else
       {
           outputFile.WriteUIntLittleEndian(0); // count = 0
       }


       for (var i = 0; i < nLayers; i++)
            outputFile.WriteSerialize(layerData[i], endianness);

       var delimiter = Delimiter;
       for (var i = 0; i < nImages; i++)
       { outputFile.WriteBytes(imageBlocks[i]); outputFile.WriteBytes(delimiter); }

       // Resin data (includes trailing CRLF) - no extra delimiter
       if (_resinData.Length > 0) outputFile.WriteBytes(_resinData);


       // MD5 of everything above
        var pos = (int)outputFile.Position;
        outputFile.Seek(0, SeekOrigin.Begin);
        var allBytes = new byte[pos];
        outputFile.ReadExactly(allBytes);
        var md5 = MD5.HashData(allBytes);
        outputFile.Seek(0, SeekOrigin.End);
        outputFile.WriteBytes(Encoding.ASCII.GetBytes(System.Convert.ToHexString(md5).ToLowerInvariant()));

        Debug.WriteLine($"V5 Encode complete, total size: {outputFile.Position}");
    }

    protected override void PartialSaveInternally(OperationProgress progress)
    {
        using var outputFile = new FileStream(TemporaryOutputFileFullPath, FileMode.Open, FileAccess.Write);
        var endianness = Endianness.Little;
        outputFile.Seek(0, SeekOrigin.Begin);
        outputFile.WriteSerialize(Header, endianness);
        for (uint layerIndex = 0; layerIndex < LayerCount; layerIndex++)
        {
            if (LayersDefinition is null || _ldtEntries is null) break;
            LayersDefinition[layerIndex].SetFrom(this[layerIndex]);
            outputFile.Seek(_ldtEntries[layerIndex].Offset, SeekOrigin.Begin);
            outputFile.WriteSerialize(LayersDefinition[layerIndex], endianness);
        }
    }

    #endregion
}
