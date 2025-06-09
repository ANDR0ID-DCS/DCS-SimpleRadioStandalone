﻿using Ciribob.DCS.SimpleRadio.Standalone.Common.Audio.Models.Dto;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Audio.Providers;
using NAudio.Dsp;
using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Text.Json.Serialization;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Audio.Models
{
    namespace Dto
    {
        [JsonDerivedType(typeof(ChainEffect), typeDiscriminator: "chain")]
        [JsonDerivedType(typeof(FiltersEffect), typeDiscriminator: "filters")]

        [JsonDerivedType(typeof(SaturationEffect), typeDiscriminator: "saturation")]
        [JsonDerivedType(typeof(CompressorEffect), typeDiscriminator: "compressor")]
        [JsonDerivedType(typeof(GainEffect), typeDiscriminator: "gain")]

        [JsonDerivedType(typeof(CVSDEffect), typeDiscriminator: "cvsd")]
        internal abstract class IEffect
        {
            public abstract ISampleProvider ToSampleProvider(ISampleProvider source);
        };

        internal class CVSDEffect : IEffect
        {
            public override ISampleProvider ToSampleProvider(ISampleProvider source)
            {
                return new CVSDProvider(source);
            }
        };

        internal class ChainEffect : IEffect
        {
            public IEffect[] Effects { get; set; }
            public override ISampleProvider ToSampleProvider(ISampleProvider source)
            {
                var last = source;
                foreach (var effect in Effects)
                {
                    last = effect.ToSampleProvider(last);
                }
                return last;
            }
        };

        internal class FiltersEffect : IEffect
        {
            public Dsp.IFilter[] Filters { get; set; }
            public override ISampleProvider ToSampleProvider(ISampleProvider source)
            {
                return new FiltersProvider(source)
                {
                    Filters = Filters
                };
            }
        };

        internal class SaturationEffect : IEffect
        {
            public float Gain { get; set; }
            public float Threshold { get; set; }

            public override ISampleProvider ToSampleProvider(ISampleProvider source)
            {
                return new SaturationProvider(source)
                {
                    GainDB = Gain,
                    ThresholdDB = Threshold
                };
            }
        };

        internal class CompressorEffect : IEffect
        {
            public float Attack { get; set; }
            public float MakeUp { get; set; }
            public float Release { get; set; }
            public float Threshold { get; set; }
            public float Slope { get; set; }

            public override ISampleProvider ToSampleProvider(ISampleProvider source)
            {
                return new SimpleCompressorEffect(source)
                {
                    Attack = Attack,
                    MakeUpGain = MakeUp,
                    Release = Release,
                    Threshold = Threshold,
                    Ratio = 1f / Slope,
                    Enabled = true,
                };
            }
        };

        internal class GainEffect : IEffect
        {
            public float Gain {  get; set; }

            public override ISampleProvider ToSampleProvider(ISampleProvider source)
            {
                return new VolumeSampleProvider(source)
                {
                    Volume = (float)Decibels.DecibelsToLinear(Gain),
                };
            }
        };

        internal class RadioPreset
        {
            public int Version { get; set; }
            public float NoiseGain { get; set; }
            public IEffect TxEffect { get; set; }
            public IEffect RxEffect { get; set; }
            public IEffect EncryptionEffect { get; set; }

        };
    }
    internal class DefaultRadioPresets
    {
        // ARC-210 as default radio FX.
        public static readonly Dto.RadioPreset Arc210 = new Dto.RadioPreset()
        {
            TxEffect = new ChainEffect()
            {
                Effects = new IEffect[]
                {
                    new FiltersEffect()
                    {
                        Filters = new Dsp.IFilter[]
                        {
                            new Dsp.BiQuadFilter()
                            {
                                Filter = BiQuadFilter.HighPassFilter(Constants.OUTPUT_SAMPLE_RATE, 1700, 0.53f),
                            },
                            new Dsp.BiQuadFilter()
                            {
                                Filter = BiQuadFilter.PeakingEQ(Constants.OUTPUT_SAMPLE_RATE, 2801, 0.5f, 5f),
                            },

                            Dsp.FirstOrderFilter.LowPass(Constants.OUTPUT_SAMPLE_RATE, 5538),
                        }
                    },

                    new SaturationEffect()
                    {
                        Gain = 9,
                        Threshold = -23,
                    },

                    new CompressorEffect()
                    {
                        Attack = 0.01f,
                        MakeUp = 6,
                        Release = 0.2f,
                        Threshold = -33,
                        Slope = 0.85f
                    },

                    new FiltersEffect()
                    {
                        Filters = new[]
                        {
                            new Dsp.BiQuadFilter()
                            {
                                Filter = BiQuadFilter.HighPassFilter(Constants.OUTPUT_SAMPLE_RATE, 456, 0.36f),
                            },
                            new Dsp.BiQuadFilter()
                            {
                                Filter = BiQuadFilter.LowPassFilter(Constants.OUTPUT_SAMPLE_RATE, 5435, 0.39f)
                            }
                        }
                    },

                    new GainEffect()
                    {
                        Gain = 12,
                    }
                }
            },

            RxEffect = new FiltersEffect()
            {
                Filters = new[]
                {
                    Dsp.FirstOrderFilter.HighPass(Constants.OUTPUT_SAMPLE_RATE, 270),
                    Dsp.FirstOrderFilter.LowPass(Constants.OUTPUT_SAMPLE_RATE, 4500)
                },
            },

            NoiseGain = -33,
        };

        public static readonly Dto.RadioPreset Intercom = new Dto.RadioPreset()
        {
            TxEffect = new ChainEffect()
            {
                Effects = new IEffect[]
                {
                    new FiltersEffect()
                    {
                        Filters = new Dsp.IFilter[]
                        {
                            new Dsp.BiQuadFilter()
                            {
                                Filter = BiQuadFilter.HighPassFilter(Constants.OUTPUT_SAMPLE_RATE, 207, 0.5f),
                            },
                            new Dsp.BiQuadFilter()
                            {
                                Filter = BiQuadFilter.PeakingEQ(Constants.OUTPUT_SAMPLE_RATE, 3112, 0.4f, 16f),
                            },
                            new Dsp.BiQuadFilter()
                            {
                                Filter = BiQuadFilter.LowPassFilter(Constants.OUTPUT_SAMPLE_RATE, 6036, 0.4f),
                            },

                            Dsp.FirstOrderFilter.LowPass(Constants.OUTPUT_SAMPLE_RATE, 5538),
                        },
                    },

                    new SaturationEffect
                    {
                        Gain = 2,
                        Threshold = -33,
                    },

                    new CompressorEffect
                    {
                        Attack = 0.01f,
                        MakeUp = -1,
                        Release = 0.2f,
                        Threshold = -17,
                        Slope = 0.85f
                    },

                    new FiltersEffect
                    {
                        Filters = new[]
                        {
                            new Dsp.BiQuadFilter()
                            {
                                Filter = BiQuadFilter.HighPassFilter(Constants.OUTPUT_SAMPLE_RATE, 393, 0.43f),
                            },
                            new Dsp.BiQuadFilter()
                            {
                                Filter = BiQuadFilter.LowPassFilter(Constants.OUTPUT_SAMPLE_RATE, 4875, 0.3f)
                            }
                        },
                    },

                    new GainEffect
                    {
                        Gain = 8,
                    }
                }
            },

            RxEffect = new FiltersEffect()
            {
                Filters = new[]
                {
                    Dsp.FirstOrderFilter.HighPass(Constants.OUTPUT_SAMPLE_RATE, 270),
                    Dsp.FirstOrderFilter.LowPass(Constants.OUTPUT_SAMPLE_RATE, 4500)
                },
            },

            NoiseGain = -60,
        };
    };
    

}
