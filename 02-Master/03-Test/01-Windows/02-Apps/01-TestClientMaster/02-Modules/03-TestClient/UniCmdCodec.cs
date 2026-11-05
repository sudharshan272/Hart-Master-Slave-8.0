/*
 *          File: UniCmdCodec.cs (CUniCmdCodec, CUniCmdResult)
 *                Encoding of request data and decoding of response data for
 *                all Hart universal commands (0..22, 38, 48). The module is
 *                written in plain C# and does not call the HartMasterDLL,
 *                so it can be used and tested independently.
 *
 * No Warranties: https://www.borst-automation.com/legal/warranty-disclaimer
 */

#region Namespaces
using BaTestHart.HartUtils;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
#endregion Namespaces

namespace BaTestHart
{
    // Result of one completed service (command), delivered to interested forms
    internal sealed class CUniCmdResult
    {
        // Command number (response command if the device answered)
        internal byte Cmd;

        // true: Device answered and the service was completed successfully
        internal bool Success;

        // Text of the client error if the service failed
        internal string ErrorText = string.Empty;

        // Command specific response code (response byte 1) and device status (byte 2)
        internal byte RespCode1;
        internal byte RespCode2;

        // Number of retries and duration of the service
        internal byte UsedRetries;
        internal short DurationMs;

        // Raw payload of the response
        internal byte[] Data = Array.Empty<byte>();

        // Human readable decoding of the payload
        internal string Decoded = string.Empty;
    }

    internal static class CUniCmdCodec
    {
        #region Command Names
        // All universal commands which are supported by the test client
        internal static readonly byte[] UniversalCommands =
        {
            0, 1, 2, 3, 6, 7, 8, 9, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 38, 48
        };

        internal static string CmdName(byte cmd_)
        {
            return cmd_ switch
            {
                0 => "Read Unique Identifier",
                1 => "Read Primary Variable",
                2 => "Read Loop Current And Percent Of Range",
                3 => "Read Dynamic Variables And Loop Current",
                6 => "Write Polling Address",
                7 => "Read Loop Configuration",
                8 => "Read Dynamic Variable Classifications",
                9 => "Read Device Variables With Status",
                11 => "Read Unique Identifier Associated With Tag",
                12 => "Read Message",
                13 => "Read Tag, Descriptor, Date",
                14 => "Read Primary Variable Transducer Information",
                15 => "Read Device Information",
                16 => "Read Final Assembly Number",
                17 => "Write Message",
                18 => "Write Tag, Descriptor, Date",
                19 => "Write Final Assembly Number",
                20 => "Read Long Tag",
                21 => "Read Unique Identifier Associated With Long Tag",
                22 => "Write Long Tag",
                38 => "Reset Configuration Changed Flag",
                48 => "Read Additional Device Status",
                _ => "Command " + cmd_.ToString(CultureInfo.InvariantCulture),
            };
        }

        // Text for the command specific response code (response byte 1)
        internal static string RespCodeText(byte code_)
        {
            if ((code_ & 0x80) != 0)
            {
                return "Communication error (0x" + code_.ToString("X2") + ")";
            }

            return code_ switch
            {
                0 => "No command-specific error",
                1 => "Undefined",
                2 => "Invalid selection",
                3 => "Passed parameter too large",
                4 => "Passed parameter too small",
                5 => "Too few data bytes received",
                6 => "Device-specific command error",
                7 => "In write-protect mode",
                8 => "Update failure / warning",
                9 => "Lower range value too high",
                10 => "Lower range value too low",
                11 => "Upper range value too high",
                12 => "Upper range value too low",
                13 => "Upper and lower range values out of limits",
                14 => "Span too small",
                16 => "Access restricted",
                32 => "Device is busy",
                33 => "Delayed response initiated",
                34 => "Delayed response running",
                35 => "Delayed response dead",
                36 => "Delayed response conflict",
                64 => "Command not implemented",
                _ => "Code " + code_.ToString(CultureInfo.InvariantCulture),
            };
        }
        #endregion Command Names

        #region Encoding
        // Pack a string into Hart packed ASCII (4 characters in 3 bytes).
        // The string is converted to upper case and padded with spaces.
        internal static byte[] EncodePackedAscii(string text_, int num_chars_)
        {
            int num_groups = (num_chars_ + 3) / 4;
            byte[] result = new byte[num_groups * 3];
            string text = (text_ ?? string.Empty).ToUpperInvariant();
            int[] c = new int[4];

            for (int g = 0; g < num_groups; g++)
            {
                for (int k = 0; k < 4; k++)
                {
                    int idx = (g * 4) + k;
                    char ch = ' ';

                    if ((idx < text.Length) && (idx < num_chars_))
                    {
                        ch = text[idx];
                        if ((ch < ' ') || (ch > '_'))
                        {
                            // Not part of the packed ASCII character set
                            ch = ' ';
                        }
                    }

                    c[k] = ch & 0x3f;
                }

                result[(g * 3) + 0] = (byte)((c[0] << 2) | (c[1] >> 4));
                result[(g * 3) + 1] = (byte)(((c[1] & 0x0f) << 4) | (c[2] >> 2));
                result[(g * 3) + 2] = (byte)(((c[2] & 0x03) << 6) | c[3]);
            }

            return result;
        }

        // Encode a string as ISO Latin-1 with a fixed length (padded with spaces)
        internal static byte[] EncodeLatin1(string text_, int len_)
        {
            string text = (text_ ?? string.Empty);

            if (text.Length > len_)
            {
                text = text.Substring(0, len_);
            }

            return Encoding.Latin1.GetBytes(text.PadRight(len_, ' '));
        }

        // Parse a list of numbers (separated by comma, semicolon or space)
        internal static bool TryParseByteList(string text_, int max_count_, out byte[] values_, out string error_)
        {
            List<byte> list = new List<byte>();
            string[] items = (text_ ?? string.Empty).Split(new[] { ',', ';', ' ' },
                                                           StringSplitOptions.RemoveEmptyEntries);

            values_ = Array.Empty<byte>();
            error_ = string.Empty;
            if (items.Length == 0)
            {
                error_ = "Enter at least one value.";
                return false;
            }

            if (items.Length > max_count_)
            {
                error_ = "At most " + max_count_.ToString(CultureInfo.InvariantCulture) + " values are allowed.";
                return false;
            }

            foreach (string item in items)
            {
                if (!byte.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte v))
                {
                    error_ = "'" + item + "' is not a number in the range 0..255.";
                    return false;
                }

                list.Add(v);
            }

            values_ = list.ToArray();
            return true;
        }
        #endregion Encoding

        #region Decoding Helpers
        // Convert packed ASCII to a string
        internal static string DecodePackedAscii(byte[] data_, int offset_, int num_chars_)
        {
            StringBuilder sb = new StringBuilder();
            int num_groups = (num_chars_ + 3) / 4;

            for (int g = 0; g < num_groups; g++)
            {
                int pos = offset_ + (g * 3);

                if ((pos + 2) >= data_.Length)
                {
                    break;
                }

                int[] c =
                {
                    data_[pos] >> 2,
                    ((data_[pos] & 0x03) << 4) | (data_[pos + 1] >> 4),
                    ((data_[pos + 1] & 0x0f) << 2) | (data_[pos + 2] >> 6),
                    data_[pos + 2] & 0x3f
                };

                for (int k = 0; k < 4; k++)
                {
                    if (sb.Length < num_chars_)
                    {
                        sb.Append((char)(c[k] < 32 ? (c[k] | 0x40) : c[k]));
                    }
                }
            }

            return sb.ToString();
        }

        internal static string DecodeLatin1(byte[] data_, int offset_, int len_)
        {
            if (offset_ >= data_.Length)
            {
                return string.Empty;
            }

            return Encoding.Latin1.GetString(data_, offset_, Math.Min(len_, data_.Length - offset_));
        }

        internal static string Hex(byte[] data_, int len_)
        {
            StringBuilder sb = new StringBuilder();

            for (int e = 0; (e < len_) && (e < data_.Length); e++)
            {
                if (e > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(data_[e].ToString("X2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        private static float PickFloat(byte[] d_, int offset_)
        {
            return BinaryPrimitives.ReadSingleBigEndian(new ReadOnlySpan<byte>(d_, offset_, 4));
        }

        private static ushort PickWord(byte[] d_, int offset_)
        {
            return BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(d_, offset_, 2));
        }

        private static string Fmt(float value_)
        {
            return value_.ToString("0.0###", CultureInfo.InvariantCulture);
        }

        private static string Unit(byte code_)
        {
            return CHartUtils.CHartUnit.GetText(code_) + " (" + code_.ToString(CultureInfo.InvariantCulture) + ")";
        }

        private static string DevVarClass(byte class_)
        {
            return class_ switch
            {
                0 => "Not classified",
                64 => "Temperature",
                65 => "Pressure",
                66 => "Volumetric flow",
                67 => "Velocity",
                68 => "Volume",
                69 => "Length",
                70 => "Time",
                71 => "Mass",
                72 => "Mass flow",
                73 => "Mass per volume",
                84 => "Current",
                _ => "Class " + class_.ToString(CultureInfo.InvariantCulture),
            };
        }

        private static string DateText(byte day_, byte month_, byte year_)
        {
            return day_.ToString("00", CultureInfo.InvariantCulture) + "." +
                   month_.ToString("00", CultureInfo.InvariantCulture) + "." +
                   (1900 + year_).ToString(CultureInfo.InvariantCulture);
        }
        #endregion Decoding Helpers

        #region Decoding
        // Decode the payload of a response.
        internal static string Decode(byte cmd_, byte[] d_, int len_)
        {
            StringBuilder sb = new StringBuilder();

            if (len_ > d_.Length)
            {
                len_ = d_.Length;
            }

            switch (cmd_)
            {
                case 0:
                case 11:
                case 21:
                    DecodeUniqueId(sb, d_, len_);
                    break;
                case 1:
                    if (Need(sb, len_, 5))
                    {
                        sb.AppendLine("PV: " + Fmt(PickFloat(d_, 1)) + " " + Unit(d_[0]));
                    }

                    break;
                case 2:
                    if (Need(sb, len_, 8))
                    {
                        sb.AppendLine("Loop current: " + Fmt(PickFloat(d_, 0)) + " mA");
                        sb.AppendLine("Percent of range: " + Fmt(PickFloat(d_, 4)) + " %");
                    }

                    break;
                case 3:
                    DecodeCmd3(sb, d_, len_);
                    break;
                case 6:
                case 7:
                    if (Need(sb, len_, 1))
                    {
                        sb.AppendLine("Polling address: " + d_[0].ToString(CultureInfo.InvariantCulture));
                    }

                    if (len_ >= 2)
                    {
                        string mode = d_[1] switch { 0 => "Disabled", 1 => "Enabled", _ => "Unknown" };

                        sb.AppendLine("Loop current mode: " + mode + " (" + d_[1].ToString(CultureInfo.InvariantCulture) + ")");
                    }

                    break;
                case 8:
                    if (Need(sb, len_, 4))
                    {
                        string[] names = { "PV", "SV", "TV", "QV" };

                        for (int e = 0; e < 4; e++)
                        {
                            sb.AppendLine(names[e] + " classification: " + DevVarClass(d_[e]));
                        }
                    }

                    break;
                case 9:
                    DecodeCmd9(sb, d_, len_);
                    break;
                case 12:
                case 17:
                    if (Need(sb, len_, 24))
                    {
                        sb.AppendLine("Message: '" + DecodePackedAscii(d_, 0, 32) + "'");
                    }

                    break;
                case 13:
                case 18:
                    if (Need(sb, len_, 21))
                    {
                        sb.AppendLine("Tag: '" + DecodePackedAscii(d_, 0, 8) + "'");
                        sb.AppendLine("Descriptor: '" + DecodePackedAscii(d_, 6, 16) + "'");
                        sb.AppendLine("Date: " + DateText(d_[18], d_[19], d_[20]));
                    }

                    break;
                case 14:
                    if (Need(sb, len_, 16))
                    {
                        uint serial = (uint)((d_[0] << 16) | (d_[1] << 8) | d_[2]);

                        sb.AppendLine("Transducer serial number: " + serial.ToString(CultureInfo.InvariantCulture));
                        sb.AppendLine("Limits and span unit: " + Unit(d_[3]));
                        sb.AppendLine("Upper transducer limit: " + Fmt(PickFloat(d_, 4)));
                        sb.AppendLine("Lower transducer limit: " + Fmt(PickFloat(d_, 8)));
                        sb.AppendLine("Minimum span: " + Fmt(PickFloat(d_, 12)));
                    }

                    break;
                case 15:
                    if (Need(sb, len_, 18))
                    {
                        sb.AppendLine("Alarm selection code: " + d_[0].ToString(CultureInfo.InvariantCulture));
                        sb.AppendLine("Transfer function code: " + d_[1].ToString(CultureInfo.InvariantCulture));
                        sb.AppendLine("Range unit: " + Unit(d_[2]));
                        sb.AppendLine("Upper range value: " + Fmt(PickFloat(d_, 3)));
                        sb.AppendLine("Lower range value: " + Fmt(PickFloat(d_, 7)));
                        sb.AppendLine("Damping value: " + Fmt(PickFloat(d_, 11)) + " s");
                        sb.AppendLine("Write protect code: " + d_[15].ToString(CultureInfo.InvariantCulture));
                        sb.AppendLine("Private label distributor: " + d_[16].ToString(CultureInfo.InvariantCulture));
                        sb.AppendLine("Analog channel flags: 0x" + d_[17].ToString("X2", CultureInfo.InvariantCulture));
                    }

                    break;
                case 16:
                case 19:
                    if (Need(sb, len_, 3))
                    {
                        uint num = (uint)((d_[0] << 16) | (d_[1] << 8) | d_[2]);

                        sb.AppendLine("Final assembly number: " + num.ToString(CultureInfo.InvariantCulture));
                    }

                    break;
                case 20:
                case 22:
                    if (Need(sb, len_, 32))
                    {
                        sb.AppendLine("Long tag: '" + DecodeLatin1(d_, 0, 32) + "'");
                    }

                    break;
                case 38:
                    sb.AppendLine("Configuration changed flag reset (no data).");
                    break;
                case 48:
                    DecodeCmd48(sb, d_, len_);
                    break;
                default:
                    break;
            }

            if (len_ > 0)
            {
                sb.AppendLine("Raw data (" + len_.ToString(CultureInfo.InvariantCulture) + "): " + Hex(d_, len_));
            }

            return sb.ToString().TrimEnd();
        }

        // Decode the result of the connection service (Cmd 0)
        internal static string DecodeConnection(HartDeviceDLL.TY_Connection c_)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Manufacturer ID: " + c_.ManuId.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Device type: " + c_.DevId.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Min. request preambles: " + c_.NumPreambles.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Hart revision: " + c_.CmdRevNum.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Device revision: " + c_.SpecRevCode.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Software revision: " + c_.SwRev.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Hardware revision: " + (c_.HwRev >> 3).ToString(CultureInfo.InvariantCulture) +
                          ", signaling code: " + (c_.HwRev & 0x07).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Flags: 0x" + c_.HartFlags.ToString("X2", CultureInfo.InvariantCulture));
            sb.AppendLine("Device ID: " + Hex(c_.BytesOfUniqueID, 5) + " (manufacturer, type, 3 bytes device number)");
            sb.AppendLine("Max. device variables: " + c_.MaxNumDVs.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Config change counter: " + c_.CfgChCount.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Extended device status: 0x" + c_.ExtDevStatus.ToString("X2", CultureInfo.InvariantCulture));
            sb.AppendLine("Extended manufacturer ID: " + c_.ExtManuID.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Private label distributor: " + c_.ExtLabDistID.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Device profile: " + c_.EDevProfile.ToString(CultureInfo.InvariantCulture));
            return sb.ToString().TrimEnd();
        }

        private static bool Need(StringBuilder sb_, int len_, int need_)
        {
            if (len_ >= need_)
            {
                return true;
            }

            sb_.AppendLine("Too few data bytes: " + len_.ToString(CultureInfo.InvariantCulture) +
                           " received, " + need_.ToString(CultureInfo.InvariantCulture) + " expected.");
            return false;
        }

        private static void DecodeUniqueId(StringBuilder sb_, byte[] d_, int len_)
        {
            if (!Need(sb_, len_, 12))
            {
                return;
            }

            sb_.AppendLine("Manufacturer ID: " + d_[1].ToString(CultureInfo.InvariantCulture));
            sb_.AppendLine("Device type: " + d_[2].ToString(CultureInfo.InvariantCulture));
            sb_.AppendLine("Min. request preambles: " + d_[3].ToString(CultureInfo.InvariantCulture));
            sb_.AppendLine("Hart revision: " + d_[4].ToString(CultureInfo.InvariantCulture));
            sb_.AppendLine("Device revision: " + d_[5].ToString(CultureInfo.InvariantCulture));
            sb_.AppendLine("Software revision: " + d_[6].ToString(CultureInfo.InvariantCulture));
            sb_.AppendLine("Hardware revision: " + (d_[7] >> 3).ToString(CultureInfo.InvariantCulture) +
                           ", signaling code: " + (d_[7] & 0x07).ToString(CultureInfo.InvariantCulture));
            sb_.AppendLine("Flags: 0x" + d_[8].ToString("X2", CultureInfo.InvariantCulture));
            sb_.AppendLine("Device ID: " + d_[9].ToString("X2", CultureInfo.InvariantCulture) +
                           d_[10].ToString("X2", CultureInfo.InvariantCulture) +
                           d_[11].ToString("X2", CultureInfo.InvariantCulture));
            if (len_ >= 17)
            {
                sb_.AppendLine("Min. response preambles: " + d_[12].ToString(CultureInfo.InvariantCulture));
                sb_.AppendLine("Max. device variables: " + d_[13].ToString(CultureInfo.InvariantCulture));
                sb_.AppendLine("Config change counter: " + PickWord(d_, 14).ToString(CultureInfo.InvariantCulture));
                sb_.AppendLine("Extended device status: 0x" + d_[16].ToString("X2", CultureInfo.InvariantCulture));
            }

            if (len_ >= 22)
            {
                sb_.AppendLine("Extended manufacturer ID: " + PickWord(d_, 17).ToString(CultureInfo.InvariantCulture));
                sb_.AppendLine("Private label distributor: " + PickWord(d_, 19).ToString(CultureInfo.InvariantCulture));
                sb_.AppendLine("Device profile: " + d_[21].ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void DecodeCmd3(StringBuilder sb_, byte[] d_, int len_)
        {
            if (!Need(sb_, len_, 4))
            {
                return;
            }

            sb_.AppendLine("Loop current: " + Fmt(PickFloat(d_, 0)) + " mA");

            string[] names = { "PV", "SV", "TV", "QV" };

            for (int e = 0; e < 4; e++)
            {
                int pos = 4 + (e * 5);

                if (len_ < (pos + 5))
                {
                    break;
                }

                sb_.AppendLine(names[e] + ": " + Fmt(PickFloat(d_, pos + 1)) + " " + Unit(d_[pos]));
            }
        }

        private static void DecodeCmd9(StringBuilder sb_, byte[] d_, int len_)
        {
            if (!Need(sb_, len_, 1))
            {
                return;
            }

            sb_.AppendLine("Extended device status: 0x" + d_[0].ToString("X2", CultureInfo.InvariantCulture));

            // Each slot has 8 bytes. A time stamp (4 bytes) may follow.
            int num_slots = (len_ - 1) / 8;

            for (int e = 0; e < num_slots; e++)
            {
                int pos = 1 + (e * 8);

                sb_.AppendLine("Slot " + e.ToString(CultureInfo.InvariantCulture) +
                               ": code " + d_[pos].ToString(CultureInfo.InvariantCulture) +
                               ", " + DevVarClass(d_[pos + 1]) +
                               ", " + Fmt(PickFloat(d_, pos + 3)) + " " + Unit(d_[pos + 2]) +
                               ", status 0x" + d_[pos + 7].ToString("X2", CultureInfo.InvariantCulture));
            }

            int rest = len_ - 1 - (num_slots * 8);

            if (rest >= 4)
            {
                uint stamp = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(d_, 1 + (num_slots * 8), 4));

                sb_.AppendLine("Time stamp: " + stamp.ToString(CultureInfo.InvariantCulture) + " (1/32 ms)");
            }
        }

        private static void DecodeCmd48(StringBuilder sb_, byte[] d_, int len_)
        {
            if (!Need(sb_, len_, 8))
            {
                return;
            }

            sb_.AppendLine("Device specific status 0..5: " + Hex(d_, 6));
            sb_.AppendLine("Extended device status: 0x" + d_[6].ToString("X2", CultureInfo.InvariantCulture));
            sb_.AppendLine("Device operating mode: " + d_[7].ToString(CultureInfo.InvariantCulture));
            if (len_ > 8)
            {
                byte[] rest = new byte[len_ - 8];

                Array.Copy(d_, 8, rest, 0, rest.Length);
                sb_.AppendLine("Further status bytes: " + Hex(rest, rest.Length));
            }
        }
        #endregion Decoding
    }
}
