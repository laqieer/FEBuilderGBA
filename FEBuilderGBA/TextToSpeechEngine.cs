using System;
using System.IO;
using System.Reflection;
using System.Speech.Synthesis;

namespace FEBuilderGBA
{
    internal static class TextToSpeechEngine
    {
        static SpeechSynthesizer s_voiceSpeech;
        static string s_currentString;

        public static int ShortLength { get; private set; }

        public static int Rate
        {
            get
            {
                return s_voiceSpeech == null ? 0 : s_voiceSpeech.Rate;
            }
        }

        public static bool TryInitialize(bool isMultibyte, out string errorMessage)
        {
            errorMessage = "";
            if (s_voiceSpeech != null)
            {
                return true;
            }

            try
            {
                s_voiceSpeech = new SpeechSynthesizer();
                s_voiceSpeech.Rate = 0;
                ShortLength = isMultibyte ? 10 : 20;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = BuildUnavailableMessage(ex);
                Log.Error(errorMessage);
                SafeDispose();
                return false;
            }
        }

        public static string[] GetInstalledVoiceLabels()
        {
            if (s_voiceSpeech == null)
            {
                return Array.Empty<string>();
            }

            try
            {
                var voices = s_voiceSpeech.GetInstalledVoices();
                string[] labels = new string[voices.Count];
                for (int i = 0; i < voices.Count; i++)
                {
                    InstalledVoice voiceperson = voices[i];
                    string language = voiceperson.VoiceInfo.Culture.Name;
                    string name = voiceperson.VoiceInfo.Name;
                    labels[i] = name + " Language:" + language;
                }
                return labels;
            }
            catch (Exception ex)
            {
                Log.Error(BuildUnavailableMessage(ex));
                SafeDispose();
                return Array.Empty<string>();
            }
        }

        public static bool IsCurrentText(string str)
        {
            return s_currentString == str;
        }

        public static bool TrySpeak(string str, bool isForce)
        {
            if (s_voiceSpeech == null)
            {
                return false;
            }

            try
            {
                if (!isForce && s_currentString == str)
                {
                    return true;
                }

                s_currentString = str;
                if (s_voiceSpeech.State == SynthesizerState.Speaking)
                {
                    s_voiceSpeech.SpeakAsyncCancelAll();
                }
                s_voiceSpeech.SpeakAsync(s_currentString);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(BuildUnavailableMessage(ex));
                SafeDispose();
                return false;
            }
        }

        public static bool TrySetRate(int rate)
        {
            if (s_voiceSpeech == null)
            {
                return false;
            }
            try
            {
                s_voiceSpeech.Rate = rate;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(BuildUnavailableMessage(ex));
                SafeDispose();
                return false;
            }
        }

        public static bool TrySetShortLength(int shortLength)
        {
            ShortLength = shortLength;
            return s_voiceSpeech != null;
        }

        public static bool TrySelectVoice(int selected)
        {
            if (s_voiceSpeech == null)
            {
                return false;
            }

            try
            {
                var installedVoices = s_voiceSpeech.GetInstalledVoices();
                if (selected >= installedVoices.Count)
                {
                    return true;
                }
                s_voiceSpeech.SelectVoice(installedVoices[selected].VoiceInfo.Name);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(BuildUnavailableMessage(ex));
                SafeDispose();
                return false;
            }
        }

        public static void Stop()
        {
            try
            {
                if (s_voiceSpeech != null)
                {
                    s_voiceSpeech.SpeakAsyncCancelAll();
                }
            }
            catch (Exception ex)
            {
                Log.Error(BuildUnavailableMessage(ex));
            }
            finally
            {
                SafeDispose();
            }
        }

        static void SafeDispose()
        {
            try
            {
                if (s_voiceSpeech != null)
                {
                    s_voiceSpeech.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Error(BuildUnavailableMessage(ex));
            }
            finally
            {
                s_voiceSpeech = null;
                s_currentString = null;
            }
        }

        static string BuildUnavailableMessage(Exception ex)
        {
            Exception root = Unwrap(ex);
            if (IsRecoverableSpeechException(root))
            {
                return "Text-to-speech is unavailable: " + root.Message;
            }
            return "Text-to-speech failed: " + R.ExceptionToString(ex);
        }

        static Exception Unwrap(Exception ex)
        {
            while ((ex is TypeInitializationException || ex is TargetInvocationException) && ex.InnerException != null)
            {
                ex = ex.InnerException;
            }
            return ex;
        }

        static bool IsRecoverableSpeechException(Exception ex)
        {
            return ex is FileNotFoundException
                || ex is FileLoadException
                || ex is TypeLoadException
                || ex is BadImageFormatException
                || ex is PlatformNotSupportedException;
        }
    }
}
