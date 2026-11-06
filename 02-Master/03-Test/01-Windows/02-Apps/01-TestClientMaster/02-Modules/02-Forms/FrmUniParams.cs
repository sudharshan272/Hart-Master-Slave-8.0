/*
 *          File: FrmUniParams.cs
 *                Request data of the universal commands 6, 9, 11, 17, 19,
 *                21 and 22 (the commands of the Commands tab which need
 *                input). The values are kept in memory only (CUniParams),
 *                they are not stored in the application settings.
 *
 *                The form is built in code (no designer file).
 *
 * No Warranties: https://www.borst-automation.com/legal/warranty-disclaimer
 */

#region Namespaces
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
#endregion Namespaces

namespace BaTestHart
{
    // Request data of the universal commands. Empty tags mean: use the tag
    // which has been read from the device (Cmd 13 for Cmd 11, Cmd 20 for Cmd 21).
    internal static class CUniParams
    {
        internal static string NewPollAddress = string.Empty;   // empty: current poll address
        internal static string DeviceVariableCodes = "0,1,2,3";
        internal static string ShortTagForCmd11 = string.Empty;
        internal static string Message = "HART TEST MESSAGE";
        internal static string FinalAssemblyNumber = "1";
        internal static string LongTagForCmd21 = string.Empty;
        internal static string LongTagToWrite = "LONG TAG TEST";

        // ---- Parsing (shows a message box on invalid input, returns false) ----

        private static bool Fail(string text_)
        {
            MessageBox.Show(text_, "Universal command", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        internal static bool TryGetCmd6(out byte addr_)
        {
            string text = NewPollAddress.Trim();

            addr_ = CSettings.PollAddress;
            if (text.Length == 0)
            {
                return true;
            }

            if (!byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out addr_) || (addr_ > 63))
            {
                return Fail("Cmd 6: the new polling address must be a number in the range 0..63.");
            }

            return true;
        }

        internal static bool TryGetCmd9(out byte[] codes_)
        {
            if (!CUniCmdCodec.TryParseByteList(DeviceVariableCodes, 8, out codes_, out string error))
            {
                return Fail("Cmd 9: " + error);
            }

            return true;
        }

        internal static bool TryGetCmd11(out string tag_)
        {
            tag_ = ShortTagForCmd11.Trim();
            if (tag_.Length == 0)
            {
                tag_ = CSettings.TagNameShort.Trim();
            }

            if (tag_.Length == 0)
            {
                return Fail("Cmd 11: no tag known. Enter a tag (Edit Cmd 6..22) or read it with Cmd 13 first.");
            }

            return true;
        }

        internal static bool TryGetCmd19(out uint number_)
        {
            if (!uint.TryParse(FinalAssemblyNumber.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number_) ||
                (number_ > 0xffffff))
            {
                return Fail("Cmd 19: the final assembly number must be in the range 0..16777215.");
            }

            return true;
        }

        internal static bool TryGetCmd21(out string tag_)
        {
            tag_ = LongTagForCmd21.TrimEnd();
            if (tag_.Trim().Length == 0)
            {
                tag_ = CSettings.TagNameLong.TrimEnd();
            }

            if (tag_.Trim().Length == 0)
            {
                return Fail("Cmd 21: no long tag known. Enter a tag (Edit Cmd 6..22) or read it with Cmd 20 first.");
            }

            return true;
        }
    }

    internal sealed class FrmUniParams : Form
    {
        private readonly FrmTestClient m_parent;
        private readonly TextBox m_txt_cmd6 = new TextBox();
        private readonly TextBox m_txt_cmd9 = new TextBox();
        private readonly TextBox m_txt_cmd11 = new TextBox();
        private readonly TextBox m_txt_cmd17 = new TextBox();
        private readonly TextBox m_txt_cmd19 = new TextBox();
        private readonly TextBox m_txt_cmd21 = new TextBox();
        private readonly TextBox m_txt_cmd22 = new TextBox();
        private int m_y = 12;

        internal FrmUniParams(FrmTestClient parent_)
        {
            m_parent = parent_;

            Text = "Request data of universal commands 6, 9, 11, 17, 19, 21, 22";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(700, 12 + (7 * 58) + 50);

            AddRow(6, "New polling address (0..63, empty = unchanged):", m_txt_cmd6,
                   CUniParams.NewPollAddress, 80,
                   "Write Polling Address. Afterwards reconnect to the new address.", m_parent.SendCmd6);
            AddRow(9, "Device variable codes (comma separated, max 8):", m_txt_cmd9,
                   CUniParams.DeviceVariableCodes, 260,
                   "Read Device Variables with Status, e.g. 0,1,2,3", m_parent.SendCmd9);
            AddRow(11, "Short tag (max 8 chars, empty = tag read by Cmd 13):", m_txt_cmd11,
                   CUniParams.ShortTagForCmd11, 160,
                   "Read Unique Identifier Associated With Tag (global address)", m_parent.SendCmd11);
            AddRow(17, "Message (max 32 chars):", m_txt_cmd17,
                   CUniParams.Message, 420,
                   "Write Message", m_parent.SendCmd17);
            AddRow(19, "Final assembly number (0..16777215):", m_txt_cmd19,
                   CUniParams.FinalAssemblyNumber, 120,
                   "Write Final Assembly Number", m_parent.SendCmd19);
            AddRow(21, "Long tag (max 32 chars, empty = tag read by Cmd 20):", m_txt_cmd21,
                   CUniParams.LongTagForCmd21, 420,
                   "Read Unique Identifier Associated With Long Tag (global address)", m_parent.SendCmd21);
            AddRow(22, "Long tag to write (max 32 chars):", m_txt_cmd22,
                   CUniParams.LongTagToWrite, 420,
                   "Write Long Tag", m_parent.SendCmd22);

            Button but_close = new Button();

            but_close.Text = "Close";
            but_close.Size = new Size(90, 28);
            but_close.Location = new Point(ClientSize.Width - 102, ClientSize.Height - 40);
            but_close.Click += (s, e) => Close();
            Controls.Add(but_close);
            AcceptButton = but_close;
        }

        private void AddRow(int cmd_, string caption_, TextBox box_, string value_, int box_width_, string hint_, Action send_)
        {
            Label caption = new Label();
            Label hint = new Label();
            Button send = new Button();

            caption.AutoSize = true;
            caption.Text = "Cmd " + cmd_ + ":  " + caption_;
            caption.Location = new Point(12, m_y);

            box_.Text = value_;
            box_.Location = new Point(12, m_y + 20);
            box_.Size = new Size(box_width_, 23);

            send.Text = "Send Cmd " + cmd_;
            send.BackColor = Color.PowderBlue;
            send.FlatStyle = FlatStyle.Popup;
            send.Size = new Size(90, 25);
            send.Location = new Point(12 + box_width_ + 10, m_y + 19);
            send.Click += (s, e) =>
            {
                StoreValues();
                send_();
            };

            hint.AutoSize = true;
            hint.ForeColor = SystemColors.GrayText;
            hint.Text = hint_;
            hint.Location = new Point(12 + box_width_ + 110, m_y + 24);
            if (hint.Left + 200 > 700)
            {
                // Not enough room beside the box: put the hint below it
                hint.Location = new Point(12, m_y + 43);
            }

            Controls.Add(caption);
            Controls.Add(box_);
            Controls.Add(send);
            Controls.Add(hint);
            m_y += 58;
        }

        // Copy the text boxes to the in-memory request data
        private void StoreValues()
        {
            CUniParams.NewPollAddress = m_txt_cmd6.Text;
            CUniParams.DeviceVariableCodes = m_txt_cmd9.Text;
            CUniParams.ShortTagForCmd11 = m_txt_cmd11.Text;
            CUniParams.Message = m_txt_cmd17.Text;
            CUniParams.FinalAssemblyNumber = m_txt_cmd19.Text;
            CUniParams.LongTagForCmd21 = m_txt_cmd21.Text;
            CUniParams.LongTagToWrite = m_txt_cmd22.Text;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StoreValues();
            base.OnFormClosing(e);
        }
    }
}
