/*
 *          File: FrmUniversalCmds.cs
 *                Window to test all Hart universal commands (0..22, 38, 48)
 *                against a slave. Every command has its own send button,
 *                commands with request data have an input field. The
 *                responses are decoded and written to a log. A "run all"
 *                function sends the whole set one after another.
 *
 *                The form is built in code (no designer file).
 *
 * No Warranties: https://www.borst-automation.com/legal/warranty-disclaimer
 */

#region Namespaces
using System.Globalization;
using System.Text;
#endregion Namespaces

namespace BaTestHart
{
    internal sealed class FrmUniversalCmds : Form
    {
        #region Private Types
        private sealed class CmdRow
        {
            internal byte Cmd;
            internal bool IsWrite;
            // The command is part of the "run all" sequence (command 6 never is)
            internal bool InRunAll;
            // The device is selected by tag and not by its unique ID
            internal bool NeedsNoConnection;
            internal Button SendButton = new Button();
            internal TextBox? Param;
            internal Action<CmdRow> Request = _ => { };
        }
        #endregion Private Types

        #region Private Data
        private readonly FrmTestClient m_parent;
        private readonly List<CmdRow> m_rows = new List<CmdRow>();
        private readonly List<CmdRow> m_queue = new List<CmdRow>();
        private readonly System.Windows.Forms.Timer m_tim = new System.Windows.Forms.Timer();
        private readonly RichTextBox m_log = new RichTextBox();
        private readonly CheckBox m_chk_write = new CheckBox();
        private readonly Button m_but_run_all = new Button();
        private readonly Label m_lbl_state = new Label();
        private int m_pending_cmd = -1;
        private int m_wait_ticks = 0;
        private bool m_reconnect_tried = false;
        private string m_last_state = string.Empty;
        #endregion Private Data

        #region Construction
        internal FrmUniversalCmds(FrmTestClient parent_)
        {
            m_parent = parent_;
            BuildForm();
            CTestClient.UniCmdCompleted += OnCmdCompleted;
            m_tim.Interval = 100;
            m_tim.Tick += TimTick;
            m_tim.Start();
        }

        private void BuildForm()
        {
            Text = "Universal Commands";
            Font = new Font("Segoe UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(960, 700);
            MinimumSize = new Size(760, 480);
            FormClosed += FrmUniversalCmds_FormClosed;

            TableLayoutPanel main = new TableLayoutPanel();
            main.Dock = DockStyle.Fill;
            main.ColumnCount = 1;
            main.RowCount = 3;
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            Controls.Add(main);

            // Tool bar
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.Padding = new Padding(6, 6, 6, 0);
            bar.WrapContents = false;

            m_but_run_all.Text = "Run all";
            m_but_run_all.Size = new Size(110, 28);
            m_but_run_all.BackColor = Color.PowderBlue;
            m_but_run_all.FlatStyle = FlatStyle.Popup;
            m_but_run_all.Click += ButRunAll_Click;
            new ToolTip().SetToolTip(m_but_run_all,
                "Send Cmd 0 and all read commands one after another. " +
                "Click again while running to stop.");
            bar.Controls.Add(m_but_run_all);

            m_chk_write.Text = "Include write commands (17, 18, 19, 22, 38) and read back";
            m_chk_write.AutoSize = true;
            m_chk_write.Margin = new Padding(12, 6, 3, 3);
            bar.Controls.Add(m_chk_write);

            Button but_clear = new Button();
            but_clear.Text = "Clear log";
            but_clear.Size = new Size(90, 28);
            but_clear.FlatStyle = FlatStyle.Popup;
            but_clear.Margin = new Padding(12, 3, 3, 3);
            but_clear.Click += (s, e) => m_log.Clear();
            bar.Controls.Add(but_clear);

            m_lbl_state.AutoSize = true;
            m_lbl_state.Margin = new Padding(16, 8, 3, 3);
            bar.Controls.Add(m_lbl_state);
            main.Controls.Add(bar, 0, 0);

            // Command rows
            Panel scroll = new Panel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            TableLayoutPanel tbl = new TableLayoutPanel();
            tbl.Dock = DockStyle.Top;
            tbl.AutoSize = true;
            tbl.ColumnCount = 3;
            tbl.Padding = new Padding(6);
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            scroll.Controls.Add(tbl);
            BuildRows();

            ToolTip tip = new ToolTip();

            for (int idx = 0; idx < m_rows.Count; idx++)
            {
                CmdRow row = m_rows[idx];

                tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                row.SendButton.Text = "Cmd " + row.Cmd.ToString(CultureInfo.InvariantCulture);
                row.SendButton.Size = new Size(72, 26);
                row.SendButton.FlatStyle = FlatStyle.Popup;
                row.SendButton.BackColor = row.IsWrite ? Color.MistyRose : Color.PowderBlue;
                row.SendButton.Margin = new Padding(3, 2, 3, 2);
                CmdRow captured = row;
                row.SendButton.Click += (s, ev) => TrySend(captured, true);
                tbl.Controls.Add(row.SendButton, 0, idx);

                Label name = new Label();
                name.Text = CUniCmdCodec.CmdName(row.Cmd) + (row.IsWrite ? "   [write]" : string.Empty);
                name.AutoSize = true;
                name.Anchor = AnchorStyles.Left;
                tbl.Controls.Add(name, 1, idx);

                if (row.Param != null)
                {
                    row.Param.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                    row.Param.Margin = new Padding(3, 3, 12, 3);
                    tbl.Controls.Add(row.Param, 2, idx);
                }
            }

            main.Controls.Add(scroll, 0, 1);

            // Log
            m_log.Dock = DockStyle.Fill;
            m_log.ReadOnly = true;
            m_log.WordWrap = false;
            m_log.Font = new Font("Consolas", 9F);
            m_log.BackColor = Color.White;
            m_log.ScrollBars = RichTextBoxScrollBars.Both;
            main.Controls.Add(m_log, 0, 2);

            AppendLog("Open a channel in the main window, send Cmd 0 (or use 'Run all'), then try the commands.",
                      Color.DimGray);
        }
        #endregion Construction

        #region Command Definitions
        private TextBox MakeParam(string text_, string hint_)
        {
            TextBox box = new TextBox();

            box.Text = text_;
            box.PlaceholderText = hint_;
            return box;
        }

        private void AddRow(byte cmd_, bool is_write_, bool in_run_all_, TextBox? param_, Action<CmdRow> request_)
        {
            CmdRow row = new CmdRow();

            row.Cmd = cmd_;
            row.IsWrite = is_write_;
            row.InRunAll = in_run_all_;
            row.NeedsNoConnection = (cmd_ == 0) || (cmd_ == 11) || (cmd_ == 21);
            row.Param = param_;
            row.Request = request_;
            m_rows.Add(row);
        }

        private void BuildRows()
        {
            AddRow(0, false, true, null, r => CTestClient.ReqCmd0short());
            AddRow(1, false, true, null, r => CTestClient.ReqCmd1());
            AddRow(2, false, true, null, r => CTestClient.ReqCmd2());
            AddRow(3, false, true, null, r => CTestClient.ReqCmd3());
            AddRow(6, true, false, MakeParam("0", "polling address 0..63"), RequestCmd6);
            AddRow(7, false, true, null, r => CTestClient.ReqCmd7());
            AddRow(8, false, true, null, r => CTestClient.ReqCmd8());
            AddRow(9, false, true, MakeParam("0,1,2,3", "device variable codes, up to 8, e.g. 0,1,2,3"), RequestCmd9);
            AddRow(11, false, true, MakeParam(string.Empty, "short tag, 8 chars (empty: tag read by Cmd 13)"), RequestCmd11);
            AddRow(12, false, true, null, r => CTestClient.ReqCmd12());
            AddRow(13, false, true, null, r => CTestClient.ReqCmd13());
            AddRow(14, false, true, null, r => CTestClient.ReqCmd14());
            AddRow(15, false, true, null, r => CTestClient.ReqCmd15());
            AddRow(16, false, true, null, r => CTestClient.ReqCmd16());
            AddRow(17, true, true, MakeParam("HART MASTER TEST", "message, up to 32 chars"), RequestCmd17);
            AddRow(18, true, true, MakeParam("TAG-1;DESCRIPTOR;01.01.2025", "tag;descriptor;DD.MM.YYYY"), RequestCmd18);
            AddRow(19, true, true, MakeParam("123456", "final assembly number 0..16777215"), RequestCmd19);
            AddRow(20, false, true, null, r => CTestClient.ReqCmd20());
            AddRow(21, false, true, MakeParam(string.Empty, "long tag, 32 chars (empty: tag read by Cmd 20)"), RequestCmd21);
            AddRow(22, true, true, MakeParam("LONG TAG TEST", "long tag, up to 32 chars"), RequestCmd22);
            AddRow(38, true, true, null, r => CTestClient.ReqCmd38());
            AddRow(48, false, true, null, r => CTestClient.ReqCmd48());
        }

        private void RequestCmd6(CmdRow row_)
        {
            if (!byte.TryParse(row_.Param?.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte addr) || (addr > 63))
            {
                AppendLog("Cmd 6: the polling address must be a number in the range 0..63.", Color.Firebrick);
                return;
            }

            CTestClient.ReqCmd6(addr);
        }

        private void RequestCmd9(CmdRow row_)
        {
            if (!CUniCmdCodec.TryParseByteList(row_.Param?.Text ?? string.Empty, 8, out byte[] codes, out string error))
            {
                AppendLog("Cmd 9: " + error, Color.Firebrick);
                return;
            }

            CTestClient.ReqCmd9(codes);
        }

        private void RequestCmd11(CmdRow row_)
        {
            string tag = (row_.Param?.Text ?? string.Empty).Trim();

            if (tag.Length == 0)
            {
                tag = CSettings.TagNameShort.Trim();
            }

            if (tag.Length == 0)
            {
                AppendLog("Cmd 11: no tag known. Type a tag or read it with Cmd 13 first.", Color.Firebrick);
                return;
            }

            CTestClient.ReqCmd11(tag);
        }

        private void RequestCmd17(CmdRow row_)
        {
            CTestClient.ReqCmd17(row_.Param?.Text ?? string.Empty);
        }

        private void RequestCmd18(CmdRow row_)
        {
            string[] parts = (row_.Param?.Text ?? string.Empty).Split(';');

            if ((parts.Length != 3) ||
                !DateTime.TryParseExact(parts[2].Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture,
                                        DateTimeStyles.None, out DateTime date) ||
                (date.Year < 1900) || (date.Year > 2155))
            {
                AppendLog("Cmd 18: use the format tag;descriptor;DD.MM.YYYY (year 1900..2155).", Color.Firebrick);
                return;
            }

            CTestClient.ReqCmd18(parts[0].Trim(), parts[1].Trim(), (byte)date.Day, (byte)date.Month, (byte)(date.Year - 1900));
        }

        private void RequestCmd19(CmdRow row_)
        {
            if (!uint.TryParse(row_.Param?.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint number) ||
                (number > 0xffffff))
            {
                AppendLog("Cmd 19: the final assembly number must be in the range 0..16777215.", Color.Firebrick);
                return;
            }

            CTestClient.ReqCmd19(number);
        }

        private void RequestCmd21(CmdRow row_)
        {
            string tag = (row_.Param?.Text ?? string.Empty).TrimEnd();

            if (tag.Length == 0)
            {
                tag = CSettings.TagNameLong.TrimEnd();
            }

            if (tag.Trim().Length == 0)
            {
                AppendLog("Cmd 21: no long tag known. Type a tag or read it with Cmd 20 first.", Color.Firebrick);
                return;
            }

            CTestClient.ReqCmd21(tag);
        }

        private void RequestCmd22(CmdRow row_)
        {
            CTestClient.ReqCmd22(row_.Param?.Text ?? string.Empty);
        }
        #endregion Command Definitions

        #region Sending
        // Try to launch the command. Returns true if a service was started.
        private bool TrySend(CmdRow row_, bool report_)
        {
            string name = "Cmd " + row_.Cmd.ToString(CultureInfo.InvariantCulture);

            if (!FrmTestClient.IsSendPossible())
            {
                if (report_)
                {
                    AppendLog(name + " not sent: open a channel in the main window first.", Color.Firebrick);
                }

                return false;
            }

            if (CTestClient.IsServicePending || (m_pending_cmd >= 0))
            {
                if (report_)
                {
                    AppendLog(name + " not sent: the previous service is still running.", Color.Firebrick);
                }

                return false;
            }

            if (!row_.NeedsNoConnection && !CTestClient.IsConnected)
            {
                if (report_)
                {
                    AppendLog(name + " not sent: not connected. Send Cmd 0 first.", Color.Firebrick);
                }

                return false;
            }

            bool launched = m_parent.RunServiceRequest(() =>
            {
                row_.Request(row_);
                return CTestClient.IsServicePending;
            });

            if (launched)
            {
                m_pending_cmd = row_.Cmd;
                m_wait_ticks = 0;
                AppendLog("> " + name + "  " + CUniCmdCodec.CmdName(row_.Cmd), Color.Navy);
            }

            return launched;
        }
        #endregion Sending

        #region Results
        private void OnCmdCompleted(CUniCmdResult result_)
        {
            string head = "< Cmd " + result_.Cmd.ToString(CultureInfo.InvariantCulture);

            if (!result_.Success)
            {
                AppendLog(head + "  FAILED: " + result_.ErrorText +
                          "  (retries " + result_.UsedRetries.ToString(CultureInfo.InvariantCulture) + ")", Color.Firebrick);
            }
            else
            {
                bool ok = (result_.RespCode1 == 0) || (result_.RespCode1 == 8);
                string line = head + (ok ? "  OK" : "  REJECTED") +
                              "  response code: " + CUniCmdCodec.RespCodeText(result_.RespCode1) +
                              ", device status 0x" + result_.RespCode2.ToString("X2", CultureInfo.InvariantCulture) +
                              ", " + result_.DurationMs.ToString(CultureInfo.InvariantCulture) + " ms" +
                              ", retries " + result_.UsedRetries.ToString(CultureInfo.InvariantCulture);

                AppendLog(line, ok ? Color.DarkGreen : Color.DarkOrange);
                if (result_.Decoded.Length > 0)
                {
                    foreach (string text in result_.Decoded.Split('\n'))
                    {
                        AppendLog("    " + text.TrimEnd('\r'), Color.Black);
                    }
                }
            }

            if (result_.Cmd == 0)
            {
                // A connection attempt is no longer a "lost connection"
                m_reconnect_tried = false;
            }

            bool was_waiting = (m_pending_cmd >= 0);

            m_pending_cmd = -1;
            if (was_waiting && (result_.Cmd == 0) && !result_.Success && (m_queue.Count > 0))
            {
                StopRun("Run stopped: no connection to the device.");
            }
        }

        private void AppendLog(string text_, Color color_)
        {
            m_log.SelectionStart = m_log.TextLength;
            m_log.SelectionLength = 0;
            m_log.SelectionColor = color_;
            m_log.AppendText(text_ + Environment.NewLine);
            m_log.SelectionColor = m_log.ForeColor;
            m_log.SelectionStart = m_log.TextLength;
            m_log.ScrollToCaret();
        }
        #endregion Results

        #region Run All
        private CmdRow? FindRow(byte cmd_)
        {
            foreach (CmdRow row in m_rows)
            {
                if (row.Cmd == cmd_)
                {
                    return row;
                }
            }

            return null;
        }

        private void ButRunAll_Click(object? sender, EventArgs e)
        {
            if (m_queue.Count > 0)
            {
                StopRun("Run stopped by user.");
                return;
            }

            if (!FrmTestClient.IsSendPossible())
            {
                AppendLog("Run not started: open a channel in the main window first.", Color.Firebrick);
                return;
            }

            // Reads first (in numeric order), then the optional writes, then
            // the read back of the values which can be written
            foreach (CmdRow row in m_rows)
            {
                if (row.InRunAll && !row.IsWrite)
                {
                    m_queue.Add(row);
                }
            }

            if (m_chk_write.Checked)
            {
                foreach (CmdRow row in m_rows)
                {
                    if (row.InRunAll && row.IsWrite)
                    {
                        m_queue.Add(row);
                    }
                }

                foreach (byte cmd in new byte[] { 12, 13, 16, 20 })
                {
                    CmdRow? row = FindRow(cmd);

                    if (row != null)
                    {
                        m_queue.Add(row);
                    }
                }
            }

            m_reconnect_tried = false;
            m_wait_ticks = 0;
            m_but_run_all.Text = "Stop";
            AppendLog("=== Run all: " + m_queue.Count.ToString(CultureInfo.InvariantCulture) + " commands ===", Color.Navy);
        }

        private void StopRun(string text_)
        {
            m_queue.Clear();
            m_but_run_all.Text = "Run all";
            AppendLog(text_, Color.Navy);
        }

        private void RunNext()
        {
            if (m_queue.Count == 0)
            {
                return;
            }

            if (!FrmTestClient.IsSendPossible())
            {
                StopRun("Run stopped: the channel was closed.");
                return;
            }

            if (m_pending_cmd >= 0)
            {
                // Waiting for the result of the last command
                m_wait_ticks++;
                if (m_wait_ticks > 200)
                {
                    m_pending_cmd = -1;
                    StopRun("Run stopped: timeout while waiting for a service.");
                }

                return;
            }

            if (CTestClient.IsServicePending)
            {
                // The main window is still busy
                return;
            }

            CmdRow row = m_queue[0];

            if (!row.NeedsNoConnection && !CTestClient.IsConnected)
            {
                // Connection lost. Try to reconnect once.
                CmdRow? connect = FindRow(0);

                if ((connect == null) || m_reconnect_tried)
                {
                    StopRun("Run stopped: no connection to the device.");
                    return;
                }

                m_reconnect_tried = true;
                m_queue.Insert(0, connect);
                return;
            }

            m_queue.RemoveAt(0);
            if (!TrySend(row, false))
            {
                AppendLog("Cmd " + row.Cmd.ToString(CultureInfo.InvariantCulture) + " skipped.", Color.DarkOrange);
            }

            if (m_queue.Count == 0)
            {
                // The last command is on its way
                m_but_run_all.Text = "Run all";
                AppendLog("=== Run all: last command sent ===", Color.Navy);
            }
        }
        #endregion Run All

        #region Timer and Closing
        private void TimTick(object? sender, EventArgs e)
        {
            bool port_open = FrmTestClient.IsSendPossible();
            bool idle = port_open && !CTestClient.IsServicePending && (m_pending_cmd < 0) && (m_queue.Count == 0);
            StringBuilder sb = new StringBuilder();

            sb.Append(port_open ? "Channel open" : "Channel closed");
            if (port_open)
            {
                sb.Append(CTestClient.IsConnected
                          ? ", connected (ID " + CUniCmdCodec.Hex(CTestClient.BytesOfUniqueId, 5) + ")"
                          : ", not connected");
            }

            string state = sb.ToString();

            if (state != m_last_state)
            {
                m_last_state = state;
                m_lbl_state.Text = state;
            }

            foreach (CmdRow row in m_rows)
            {
                row.SendButton.Enabled = idle;
            }

            RunNext();
        }

        private void FrmUniversalCmds_FormClosed(object? sender, FormClosedEventArgs e)
        {
            m_tim.Stop();
            m_tim.Tick -= TimTick;
            m_tim.Dispose();
            CTestClient.UniCmdCompleted -= OnCmdCompleted;
        }
        #endregion Timer and Closing
    }
}
