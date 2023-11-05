﻿using System;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using MathNet.Filtering;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Ciribob.DCS.SimpleRadio.Standalone.Client.DSP;
using FragLabs.Audio.Codecs;
using NLog;
using static Ciribob.DCS.SimpleRadio.Standalone.Common.RadioInformation;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Recording;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using System.Collections.Generic;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class ClientAudioProvider : AudioProvider
    {
        private readonly Random _random = new Random();

        public static readonly int SILENCE_PAD = 200;

        private OpusDecoder _decoder;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly CachedAudioEffectProvider audioEffectProvider = CachedAudioEffectProvider.Instance;

        private bool passThrough;
       // private readonly WaveFileWriter waveWriter;
        public ClientAudioProvider(bool passThrough = false)
        {
            this.passThrough = passThrough;

            if (!passThrough)
            {
                var radios = ClientStateSingleton.Instance.DcsPlayerRadioInfo.radios.Length;
                JitterBufferProviderInterface =
                    new JitterBufferProviderInterface[radios];

                for (int i = 0;  i < radios; i++)
                {
                    JitterBufferProviderInterface[i] =
                        new JitterBufferProviderInterface(new WaveFormat(AudioManager.OUTPUT_SAMPLE_RATE, 1));

                }
                
            }
           // waveWriter = new NAudio.Wave.WaveFileWriter($@"C:\\temp\\output{RandomFloat()}.wav", new WaveFormat(AudioManager.OUTPUT_SAMPLE_RATE, 1));
            
            _decoder = OpusDecoder.Create(AudioManager.OUTPUT_SAMPLE_RATE, 1);
            _decoder.ForwardErrorCorrection = false;
            _decoder.MaxDataBytes = AudioManager.OUTPUT_SAMPLE_RATE * 4;
        }

        public JitterBufferProviderInterface[] JitterBufferProviderInterface { get; }

        public long LastUpdate { get; private set; }

        //is it a new transmission?
        public bool LikelyNewTransmission()
        {
            if (passThrough)
            {
                return false;
            }

            //400 ms since last update
            long now = DateTime.Now.Ticks;
            if ((now - LastUpdate) > 4000000) //400 ms since last update
            {
                return true;
            }

            return false;
        }

        public JitterBufferAudio AddClientAudioSamples(ClientAudio audio)
        {

            //sort out volume
            //            var timer = new Stopwatch();
            //            timer.Start();

            bool newTransmission = LikelyNewTransmission();

            //TODO reduce the size of this buffer
            var decoded = _decoder.DecodeFloat(audio.EncodedAudio,
                audio.EncodedAudio.Length, out var decodedLength, newTransmission);

            if (decodedLength <= 0)
            {
                Logger.Info("Failed to decode audio from Packet for client");
                return null;
            }

            // for some reason if this is removed then it lags?!
            //guess it makes a giant buffer and only uses a little?
            //Answer: makes a buffer of 4000 bytes - so throw away most of it

            //TODO reuse this buffer
            var tmp = new float[decodedLength/4];
            Buffer.BlockCopy(decoded, 0, tmp, 0, decodedLength);

            //convert the byte buffer to a wave buffer
         //   var waveBuffer = new WaveBuffer(tmp);

       
            
            audio.PcmAudioFloat = tmp;

            var decrytable = audio.Decryptable /* || (audio.Encryption == 0) <--- this test has already been performed by all callers and would require another call to check for STRICT_AUDIO_ENCRYPTION */;

            if (decrytable)
            {
                //adjust for LOS + Distance + Volume
                AdjustVolumeForLoss(audio);

                //Add cockpit effect
                AddCockpitAmbientAudio(audio);
            }
            else
            {
                AddEncryptionFailureEffect(audio);
            }

            if (newTransmission)
            {
                // System.Diagnostics.Debug.WriteLine(audio.ClientGuid+"ADDED");
                //append ms of silence - this functions as our jitter buffer??
                var silencePad = (AudioManager.OUTPUT_SAMPLE_RATE / 1000) * SILENCE_PAD;
                var newAudio = new float[audio.PcmAudioFloat.Length + silencePad];
                Buffer.BlockCopy(audio.PcmAudioFloat, 0, newAudio, silencePad, audio.PcmAudioFloat.Length);
                audio.PcmAudioFloat = newAudio;
            }

            LastUpdate = DateTime.Now.Ticks;

            if (audio.OriginalClientGuid == ClientStateSingleton.Instance.ShortGUID)
            {
                // catch own transmissions and prevent them from being added to JitterBuffer unless its passthrough
                if (passThrough)
                {
                    //return MONO PCM 16 as bytes
                    return new JitterBufferAudio
                    {
                        Audio = audio.PcmAudioFloat,
                        PacketNumber = audio.PacketNumber,
                        Decryptable = decrytable,
                        Modulation = (Modulation)audio.Modulation,
                        ReceivedRadio = audio.ReceivedRadio,
                        Volume = audio.Volume,
                        IsSecondary = audio.IsSecondary,
                        Frequency = audio.Frequency,
                        NoAudioEffects = audio.NoAudioEffects,
                        Guid = audio.ClientGuid,
                        OriginalClientGuid = audio.OriginalClientGuid,
                        Encryption = audio.Encryption
                    };
                }
                else
                {
                    return null;
                }

            }
            else if (!passThrough)
            {
                JitterBufferProviderInterface[audio.ReceivedRadio].AddSamples(new JitterBufferAudio
                {
                    Audio = audio.PcmAudioFloat,
                    PacketNumber = audio.PacketNumber,
                    Decryptable = decrytable,
                    Modulation = (Modulation) audio.Modulation,
                    ReceivedRadio = audio.ReceivedRadio,
                    Volume = audio.Volume,
                    IsSecondary = audio.IsSecondary,
                    Frequency = audio.Frequency,
                    NoAudioEffects = audio.NoAudioEffects,
                    Guid = audio.ClientGuid,
                    OriginalClientGuid = audio.OriginalClientGuid,
                    Encryption = audio.Encryption
                });

                return null;
            }
            else
            {
                //return MONO PCM 32 as bytes
                return new JitterBufferAudio
                {
                    Audio = audio.PcmAudioFloat,
                    PacketNumber = audio.PacketNumber,
                    Decryptable = decrytable,
                    Modulation = (Modulation)audio.Modulation,
                    ReceivedRadio = audio.ReceivedRadio,
                    Volume = audio.Volume,
                    IsSecondary = audio.IsSecondary,
                    Frequency = audio.Frequency,
                    NoAudioEffects = audio.NoAudioEffects,
                    Guid = audio.ClientGuid,
                    OriginalClientGuid = audio.OriginalClientGuid,
                    Encryption = audio.Encryption
                };
            }

            //timer.Stop();
        }

        //progress
        private readonly Dictionary<string, int> ambientEffectProgress =
            new Dictionary<string, int>();

        private void AddCockpitAmbientAudio(ClientAudio clientAudio)
        {
            var effect = audioEffectProvider.GetAmbientEffect(clientAudio.Ambient.abType);

            var vol = clientAudio.Ambient.vol;

            if (effect.Loaded)
            {
                var effectLength = effect.AudioEffectFloat.Length;

                int progress = ambientEffectProgress[clientAudio.Ambient.abType];

                var audio = clientAudio.PcmAudioFloat;
                for (var i = 0; i < audio.Length; i++)
                {
                    var audioFloat = audio[i];

                    audioFloat += (effect.AudioEffectFloat[progress] * vol);

                    audio[i] = audioFloat;

                    progress++;

                    if (progress >= effectLength)
                    {
                        progress = 0;
                    }
                }

                ambientEffectProgress[clientAudio.Ambient.abType] = progress;
            }
        }

        private void AdjustVolumeForLoss(ClientAudio clientAudio)
        {
            if (clientAudio.Modulation == (short)Modulation.MIDS || clientAudio.Modulation == (short)Modulation.SATCOM)
            {
                return;
            }

            var audio = clientAudio.PcmAudioFloat;
            for (var i = 0; i < audio.Length; i++)
            {
                var audioFloat = audio[i];

                //add in radio loss
                //if less than loss reduce volume
                if (clientAudio.RecevingPower > 0.85) // less than 20% or lower left
                {
                    //gives linear signal loss from 15% down to 0%
                    audioFloat = (float)(audioFloat * (1.0f - clientAudio.RecevingPower));
                }

                //0 is no loss so if more than 0 reduce volume
                if (clientAudio.LineOfSightLoss > 0)
                {
                    audioFloat = (audioFloat * (1.0f - clientAudio.LineOfSightLoss));
                }

                audio[i] = audioFloat;
            }
        }
        private void AddEncryptionFailureEffect(ClientAudio clientAudio)
        {
            var mixedAudio = clientAudio.PcmAudioFloat;

            for (var i = 0; i < mixedAudio.Length; i++)
            {
                mixedAudio[i] = RandomFloat();
            }
        }


        private float RandomFloat()
        {
            //random float at max volume at eights
            float f = ((float)_random.Next(-32768 / 8, 32768 / 8)) / (float)32768;
            if (f > 1) f = 1;
            if (f < -1) f = -1;
         
            return f;
        }


        //destructor to clear up opus
        ~ClientAudioProvider()
        {
            // waveWriter.Flush();
            // waveWriter.Dispose();
            _decoder?.Dispose();
            _decoder = null;
        }

    }
}