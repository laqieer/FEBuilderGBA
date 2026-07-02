using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    public partial class TextToSpeechForm : Form
    {
        internal delegate bool TryInitializeSpeechHandler(bool isMultibyte, out string errorMessage);

        static bool s_speechInitialized;
        static TryInitializeSpeechHandler s_tryInitializeSpeech = TryInitializeSpeech;
        string DefString;
        bool IsEmulatorMode;

        public TextToSpeechForm()
        {
            InitializeComponent();

            this.IconPictureBox.Image = Properties.Resources.icon_speaker.ToBitmap();
            U.AddCancelButton(this);
        }

        public void SetDefString(string str)
        {
            DefString = str;
        }

        public void SetEmulatorMode(bool isEmulatorMode)
        {
            this.IsEmulatorMode = isEmulatorMode;
        }

        private void TextToSpeechForm_Load(object sender, EventArgs e)
        {
            if (!Init())
            {
                this.Close();
                return;
            }

            Rate.Value = GetSpeechRate();
            ShortNum.Value = GetSpeechShortLength();

            if (this.IsEmulatorMode)
            {
                ShortNumLabel.Show();
                ShortNum.Show();
            }
            else
            {
                ShortNumLabel.Hide();
                ShortNum.Hide();
            }

            this.VoiceComboBox.BeginUpdate();
            this.VoiceComboBox.Items.Clear();
            foreach (string voice in GetInstalledVoiceLabels())
            {
                this.VoiceComboBox.Items.Add(voice);
            }
            this.VoiceComboBox.EndUpdate();
        }

        bool Init()
        {
            string errorMessage;
            if (!EnsureSpeechInitialized(Program.ROM.RomInfo.is_multibyte, out errorMessage))
            {
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    R.ShowStopError(errorMessage);
                }
                return false;
            }

            return true;
        }

        internal static bool EnsureSpeechInitialized(bool isMultibyte, out string errorMessage)
        {
            if (!s_tryInitializeSpeech(isMultibyte, out errorMessage))
            {
                s_speechInitialized = false;
                return false;
            }

            s_speechInitialized = true;
            return true;
        }

        public static string TextJoinCopy(string str, bool useSentensLineBreak)
        {
            return TextToSpeechTextUtil.TextJoinCopy(str, useSentensLineBreak);
        }

        public static void Speak(string str, bool isForce = false)
        {
            if (!s_speechInitialized)
            {
                return;
            }

            str = TextToSpeechTextUtil.TextJoinCopy(str, useSentensLineBreak: false);
            if (str.Length <= 0)
            {
                return;
            }

            if (!isForce && IsCurrentSpeechText(str))
            {
                return;
            }
            if (str.Length < GetSpeechShortLength())
            {
                return;
            }

            if (!TrySpeakInitialized(str, isForce))
            {
                s_speechInitialized = false;
            }
        }

        public static void Stop()
        {
            if (!s_speechInitialized)
            {
                return;
            }

            StopInitializedSpeech();
            s_speechInitialized = false;
        }

        private void EndButton_Click(object sender, EventArgs e)
        {
            Stop();
            this.Close();
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            if (!Init())
            {
                return;
            }
            Speak(this.DefString, true);
            this.Close();
        }

        private void Rate_ValueChanged(object sender, EventArgs e)
        {
            if (!s_speechInitialized)
            {
                return;
            }
            if (!TrySetSpeechRate((int)Rate.Value))
            {
                s_speechInitialized = false;
            }
        }

        private void ShortNum_ValueChanged(object sender, EventArgs e)
        {
            if (!s_speechInitialized)
            {
                return;
            }
            if (!TrySetSpeechShortLength((int)ShortNum.Value))
            {
                s_speechInitialized = false;
            }
        }

        public static bool OptionTextToSpeech(string text, bool isEmulatorMode = false)
        {
            TextToSpeechForm f = (TextToSpeechForm)InputFormRef.JumpFormLow<TextToSpeechForm>();
            text = TextForm.StripAllCode(TextForm.ConvertEscapeTextRev(text));
            f.SetDefString(text);
            f.SetEmulatorMode(isEmulatorMode);
            f.ShowDialog();

            return s_speechInitialized;
        }

        private void VoiceComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!s_speechInitialized)
            {
                return;
            }

            int selected = this.VoiceComboBox.SelectedIndex;
            if (selected < 0)
            {
                return;
            }
            if (!TrySelectVoice(selected))
            {
                s_speechInitialized = false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool TryInitializeSpeech(bool isMultibyte, out string errorMessage)
        {
            try
            {
                return TextToSpeechEngine.TryInitialize(isMultibyte, out errorMessage);
            }
            catch (Exception ex)
            {
                errorMessage = BuildSpeechUnavailableMessage(ex);
                Log.Error(errorMessage);
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int GetSpeechRate()
        {
            try
            {
                return TextToSpeechEngine.Rate;
            }
            catch (Exception ex)
            {
                Log.Error(BuildSpeechUnavailableMessage(ex));
                return 0;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int GetSpeechShortLength()
        {
            try
            {
                return TextToSpeechEngine.ShortLength;
            }
            catch (Exception ex)
            {
                Log.Error(BuildSpeechUnavailableMessage(ex));
                return int.MaxValue;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static string[] GetInstalledVoiceLabels()
        {
            try
            {
                string[] labels = TextToSpeechEngine.GetInstalledVoiceLabels();
                s_speechInitialized = IsSpeechEngineInitialized();
                return labels;
            }
            catch (Exception ex)
            {
                Log.Error(BuildSpeechUnavailableMessage(ex));
                s_speechInitialized = false;
                return Array.Empty<string>();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool IsSpeechEngineInitialized()
        {
            try
            {
                return TextToSpeechEngine.IsInitialized;
            }
            catch (Exception ex)
            {
                Log.Error(BuildSpeechUnavailableMessage(ex));
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool IsCurrentSpeechText(string str)
        {
            try
            {
                return TextToSpeechEngine.IsCurrentText(str);
            }
            catch (Exception ex)
            {
                Log.Error(BuildSpeechUnavailableMessage(ex));
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool TrySpeakInitialized(string str, bool isForce)
        {
            try
            {
                return TextToSpeechEngine.TrySpeak(str, isForce);
            }
            catch (Exception ex)
            {
                Log.Error(BuildSpeechUnavailableMessage(ex));
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool TrySetSpeechRate(int rate)
        {
            try
            {
                return TextToSpeechEngine.TrySetRate(rate);
            }
            catch (Exception ex)
            {
                Log.Error(BuildSpeechUnavailableMessage(ex));
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool TrySetSpeechShortLength(int shortLength)
        {
            try
            {
                return TextToSpeechEngine.TrySetShortLength(shortLength);
            }
            catch (Exception ex)
            {
                Log.Error(BuildSpeechUnavailableMessage(ex));
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool TrySelectVoice(int selected)
        {
            try
            {
                return TextToSpeechEngine.TrySelectVoice(selected);
            }
            catch (Exception ex)
            {
                Log.Error(BuildSpeechUnavailableMessage(ex));
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void StopInitializedSpeech()
        {
            try
            {
                TextToSpeechEngine.Stop();
            }
            catch (Exception ex)
            {
                Log.Error(BuildSpeechUnavailableMessage(ex));
            }
        }

        static string BuildSpeechUnavailableMessage(Exception ex)
        {
            Exception root = UnwrapSpeechException(ex);
            if (root is FileNotFoundException
                || root is FileLoadException
                || root is TypeLoadException
                || root is BadImageFormatException
                || root is PlatformNotSupportedException)
            {
                return "Text-to-speech is unavailable: " + root.Message;
            }
            return "Text-to-speech failed: " + R.ExceptionToString(ex);
        }

        static Exception UnwrapSpeechException(Exception ex)
        {
            while ((ex is TypeInitializationException || ex is TargetInvocationException) && ex.InnerException != null)
            {
                ex = ex.InnerException;
            }
            return ex;
        }

        internal static bool IsSpeechInitializedForTests
        {
            get
            {
                return s_speechInitialized;
            }
        }

        internal static void SetSpeechInitializedForTests(bool initialized)
        {
            s_speechInitialized = initialized;
        }

        internal static void SetTryInitializeSpeechForTests(TryInitializeSpeechHandler handler)
        {
            s_tryInitializeSpeech = handler ?? TryInitializeSpeech;
        }

        internal static void ResetSpeechTestHooksForTests()
        {
            s_speechInitialized = false;
            s_tryInitializeSpeech = TryInitializeSpeech;
        }
    }
}
