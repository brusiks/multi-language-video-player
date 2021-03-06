﻿using System;
using System.Drawing;
using System.Windows.Forms;
using MultiLanguageVideoPlayer.Helpers;
using MultiLanguageVideoPlayer.Model;
using Timer = System.Timers.Timer;

namespace MultiLanguageVideoPlayer
{
    public partial class MainForm : Form
    {
        private string _filePath;

        private readonly VlcManager _vlcManager = new VlcManager();
        private PlayStatus _playStatus;
        private VideoInfo _videoInfo;
        private readonly object _lockObject = new object();
        private bool _blockVideoPositionValueChange;
        private int _timeStatusTimeDifference;
        private readonly ToolTip _toolTip = new ToolTip();

        private bool IsFirstPlayerMain => LeftVideoRB.Checked;

        public MainForm()
        {
            InitializeComponent();
            SetupTimerSync();
            InitDefaultValuesFromSettings();

            Application.ApplicationExit += (_, __) =>
            {
                if (_vlcManager.IsInitialized)
                    VlcClient.Stop();
                _vlcManager.Stop();
            };

            if (!string.IsNullOrWhiteSpace(VlcPathText.Text))
            {
                TryInitVlcManager();
            }
        }

        private void TryInitVlcManager()
        {
            if (!_vlcManager.Init())
            {
                MessageBox.Show(@"Path to VLC is incorrect");
                VlcPathText.Text = string.Empty;
                Properties.Settings.Default.VlcPath = string.Empty;
                Properties.Settings.Default.Save();
            }
        }

        private void InitDefaultValuesFromSettings()
        {
            if (!string.IsNullOrEmpty(Properties.Settings.Default.VlcPath))
                VlcPathText.Text = Properties.Settings.Default.VlcPath;

            if (Properties.Settings.Default.DefaultVideoConfiguration == 0)
            {
                LeftVideoRB.Checked = true;
            }
            else
            {
                RightVideoRB.Checked = true;
            }
        }

        private void SetupTimerSync()
        {
            var timer = new Timer(1000);
            timer.Elapsed += (sender, args) =>
            {
                lock (_lockObject)
                {
                    if (_playStatus == PlayStatus.Playing && _vlcManager.IsInitialized)
                    {
                        var timeStatus = VlcClient.SyncTime(IsFirstPlayerMain);
                        UpdateVideoPosition(timeStatus.CurrentTime);
                        _timeStatusTimeDifference = timeStatus.TimeDifference;
                    }
                }
            };
            timer.AutoReset = true;
            timer.Enabled = true;
        }

        private void UpdateVideoPosition(int time)
        {
            if (!_blockVideoPositionValueChange)
            {
                VideoPosition.Invoke((MethodInvoker) delegate { VideoPosition.Value = time; });
            }
        }

        private void UpdateUi()
        {
            if (!string.IsNullOrEmpty(Properties.Settings.Default.VlcPath) && !string.IsNullOrEmpty(_filePath) &&
                LeftAudioTrack.SelectedIndex >= 0 && LeftAudioDevice.SelectedIndex >= 0 &&
                RightAudioTrack.SelectedIndex >= 0 && RightAudioDevice.SelectedIndex >= 0)
            {
                PlayButton.Enabled = _playStatus != PlayStatus.Playing;
                StopButton.Enabled = _playStatus != PlayStatus.Stopped;
                PauseButton.Enabled = _playStatus == PlayStatus.Playing;
            }
            else
            {
                PlayButton.Enabled = false;
                StopButton.Enabled = false;
                PauseButton.Enabled = false;
            }
        }

        private void FileBrowseButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(VlcPathText.Text))
                return;

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                if (_playStatus != PlayStatus.Stopped)
                    StopButton_Click(null, null);

                SelectedFileLabel.Visible = true;
                _filePath = openFileDialog.FileName;
                SelectedFileLabel.Text = openFileDialog.FileName;

                _videoInfo = VlcClient.GetFileInfo(openFileDialog.FileName);

                Text = _videoInfo.Title;

                LeftAudioTrack.Items.Clear();
                RightAudioTrack.Items.Clear();
                LeftAudioDevice.Items.Clear();
                RightAudioDevice.Items.Clear();

                foreach (var audioTrack in _videoInfo.AudioTracks)
                {
                    LeftAudioTrack.Items.Add(audioTrack);
                    RightAudioTrack.Items.Add(audioTrack);
                }

                foreach (var audioDevice in _videoInfo.AudioDevices)
                {
                    LeftAudioDevice.Items.Add(audioDevice.Key);
                    RightAudioDevice.Items.Add(audioDevice.Key);
                }

                VideoPosition.SetRange(0, _videoInfo.Duration);

                VideoTimeLabel.Visible = true;
                VideoPosition.Enabled = true;
                UpdateVideoTime();

                ConfigurationPanel.Enabled = true;

                if (Properties.Settings.Default.DefaultLeftDevice >= 0 &&
                    Properties.Settings.Default.DefaultLeftDevice < LeftAudioDevice.Items.Count)
                    LeftAudioDevice.SelectedIndex = Properties.Settings.Default.DefaultLeftDevice;

                if (Properties.Settings.Default.DefaultRightDevice >= 0 &&
                    Properties.Settings.Default.DefaultRightDevice < RightAudioDevice.Items.Count)
                    RightAudioDevice.SelectedIndex = Properties.Settings.Default.DefaultRightDevice;

                if (Properties.Settings.Default.DefaultLeftTrack >= 0 &&
                    Properties.Settings.Default.DefaultLeftTrack < LeftAudioTrack.Items.Count)
                    LeftAudioTrack.SelectedIndex = Properties.Settings.Default.DefaultLeftTrack;

                if (Properties.Settings.Default.DefaultRightTrack >= 0 &&
                    Properties.Settings.Default.DefaultRightTrack < RightAudioTrack.Items.Count)
                    RightAudioTrack.SelectedIndex = Properties.Settings.Default.DefaultRightTrack;
            }
        }

        private void UpdateVideoTime()
        {
            var text =
                $@"{TimeHelper.SecondsToString(VideoPosition.Value)} - {TimeHelper.SecondsToString(_videoInfo.Duration)}";
            _toolTip.SetToolTip(VideoTimeLabel, $"{text}\nAudio time difference: {_timeStatusTimeDifference} sec");
            VideoTimeLabel.Text = $@"{text} ({_timeStatusTimeDifference})";

            VideoTimeLabel.ForeColor = _timeStatusTimeDifference != 0 ? Color.Red : Color.Black;
        }

        private void PlayButton_Click(object sender, EventArgs e)
        {
            if (_playStatus == PlayStatus.Stopped)
            {
                VlcClient.AddFile(_filePath, IsFirstPlayerMain);
                VlcClient.SetAudioTracks(LeftAudioTrack.SelectedIndex + 1, RightAudioTrack.SelectedIndex + 1);

                var leftAudioDevice = _videoInfo.AudioDevices[LeftAudioDevice.SelectedItem.ToString()];
                var rightAudioDevice = _videoInfo.AudioDevices[RightAudioDevice.SelectedItem.ToString()];
                VlcClient.SetAudioDevices(leftAudioDevice, rightAudioDevice);
                VlcClient.SeekTo(VideoPosition.Value);
            }
            else if (_playStatus == PlayStatus.Paused)
            {
                VlcClient.SeekTo(VideoPosition.Value);
                VlcClient.TogglePause();
            }

            _playStatus = PlayStatus.Playing;
            UpdateUi();
        }

        private void RefreshPlayButton_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateUi();

            if (sender is ListBox listBox)
            {
                if (listBox == LeftAudioDevice)
                {
                    Properties.Settings.Default.DefaultLeftDevice = LeftAudioDevice.SelectedIndex;
                    Properties.Settings.Default.Save();
                }

                if (listBox == RightAudioDevice)
                {
                    Properties.Settings.Default.DefaultRightDevice = RightAudioDevice.SelectedIndex;
                    Properties.Settings.Default.Save();
                }

                if (listBox == LeftAudioTrack)
                {
                    Properties.Settings.Default.DefaultLeftTrack = LeftAudioTrack.SelectedIndex;
                    Properties.Settings.Default.Save();
                }

                if (listBox == RightAudioTrack)
                {
                    Properties.Settings.Default.DefaultRightTrack = RightAudioTrack.SelectedIndex;
                    Properties.Settings.Default.Save();
                }
            }
        }

        private void VlcPathBrowse_Click(object sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                VlcPathText.Text = openFileDialog.FileName;
                Properties.Settings.Default.VlcPath = openFileDialog.FileName;
                Properties.Settings.Default.Save();
                TryInitVlcManager();
            }
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            if (_playStatus == PlayStatus.Paused)
                VlcClient.TogglePause();

            VlcClient.Stop();

            _playStatus = PlayStatus.Stopped;
            UpdateUi();
        }

        private void PauseButton_Click(object sender, EventArgs e)
        {
            _playStatus = PlayStatus.Paused;
            UpdateUi();

            VlcClient.TogglePause();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_vlcManager.IsInitialized)
                VlcClient.Stop();
            _vlcManager.Stop();
        }

        private void VideoPosition_MouseDown(object sender, MouseEventArgs e)
        {
            _blockVideoPositionValueChange = true;
        }

        private void VideoPosition_MouseUp(object sender, MouseEventArgs e)
        {
            if (!_blockVideoPositionValueChange)
                return;

            _blockVideoPositionValueChange = false;

            if (_playStatus == PlayStatus.Playing)
            {
                VlcClient.SeekTo(VideoPosition.Value);
            }
        }

        private void VideoPosition_ValueChanged(object sender, EventArgs e)
        {
            UpdateVideoTime();
        }

        private void VideoRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            if ((RadioButton) sender == LeftVideoRB)
            {
                Properties.Settings.Default.DefaultVideoConfiguration = 0;
                Properties.Settings.Default.Save();
            }
            else if ((RadioButton) sender == RightVideoRB)
            {
                Properties.Settings.Default.DefaultVideoConfiguration = 1;
                Properties.Settings.Default.Save();
            }
        }
    }
}