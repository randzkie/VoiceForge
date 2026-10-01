using System;
using System.Collections.Generic;

namespace VoiceForge.Models
{
    /// <summary>
    /// Description of one voice effect chain. Every engine effect is optional and
    /// driven purely by these parameters, so presets are just data.
    /// </summary>
    public class VoicePreset
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";
        public string Badge = "";        // Short glyph shown on the voice card.

        public double PitchSemitones;    // -12..+12, 0 = off
        public double RingModFreq;       // Hz, 0 = off
        public double ReverbMix;         // 0..1
        public double ReverbDecay;       // seconds 0.2..6
        public double EchoDelayMs;       // 0 = off
        public double EchoFeedback;      // 0..0.9
        public double EchoMix;           // 0..1
        public double DistortionDrive;   // 1 = off
        public string Filter = "none";   // "none" | "lowpass" | "bandpass" | "highpass"
        public double FilterFreq;        // Hz
        public double FilterQ;
        public bool FlangerOn;
        public double FlangerRate;       // Hz
        public double FlangerDepth;      // 0..1
        public double PreGain = 1.0;

        public VoicePreset Clone()
        {
            VoicePreset c = new VoicePreset();
            c.Id = Id;
            c.Name = Name;
            c.Description = Description;
            c.Badge = Badge;
            c.PitchSemitones = PitchSemitones;
            c.RingModFreq = RingModFreq;
            c.ReverbMix = ReverbMix;
            c.ReverbDecay = ReverbDecay;
            c.EchoDelayMs = EchoDelayMs;
            c.EchoFeedback = EchoFeedback;
            c.EchoMix = EchoMix;
            c.DistortionDrive = DistortionDrive;
            c.Filter = Filter;
            c.FilterFreq = FilterFreq;
            c.FilterQ = FilterQ;
            c.FlangerOn = FlangerOn;
            c.FlangerRate = FlangerRate;
            c.FlangerDepth = FlangerDepth;
            c.PreGain = PreGain;
            return c;
        }
    }

    public static class VoiceCatalog
    {
        private static VoicePreset Make(
            string id, string name, string desc, string badge,
            double pitch, double ring, double reverbMix, double reverbDecay,
            double echoMs, double echoFb, double echoMix,
            double drive, string filter, double fFreq, double fQ,
            bool flanger, double flRate, double flDepth, double preGain)
        {
            VoicePreset p = new VoicePreset();
            p.Id = id;
            p.Name = name;
            p.Description = desc;
            p.Badge = badge;
            p.PitchSemitones = pitch;
            p.RingModFreq = ring;
            p.ReverbMix = reverbMix;
            p.ReverbDecay = reverbDecay;
            p.EchoDelayMs = echoMs;
            p.EchoFeedback = echoFb;
            p.EchoMix = echoMix;
            p.DistortionDrive = drive;
            p.Filter = filter;
            p.FilterFreq = fFreq;
            p.FilterQ = fQ;
            p.FlangerOn = flanger;
            p.FlangerRate = flRate;
            p.FlangerDepth = flDepth;
            p.PreGain = preGain;
            return p;
        }

        /// <summary>All 13 preset voices + the Custom slot (always last).</summary>
        public static List<VoicePreset> GetAll()
        {
            List<VoicePreset> list = new List<VoicePreset>();

            list.Add(Make("helium", "Helium", "Squeaky balloon voice", "He",
                6.0, 0, 0, 0, 0, 0, 0, 1.0, "none", 0, 0, false, 0, 0, 1.0));

            list.Add(Make("chipmunk", "Chipmunk", "Cartoon-fast chatter", "Cm",
                8.5, 0, 0, 0, 0, 0, 0, 1.0, "none", 0, 0, false, 0, 0, 0.9));

            list.Add(Make("girl", "Girl", "Sweet feminine timbre", "Gl",
                4.0, 0, 0.10, 1.1, 0, 0, 0, 1.0, "highpass", 130, 0.8, false, 0, 0, 1.05));

            list.Add(Make("darth", "Deep Vader", "Dark and menacing", "Dv",
                -4.5, 0, 0.18, 1.8, 0, 0, 0, 1.0, "none", 0, 0, false, 0, 0, 1.1));

            list.Add(Make("robot", "Robot", "Metallic android", "Rb",
                0, 45, 0, 0, 0, 0, 0, 1.8, "none", 0, 0, false, 0, 0, 1.0));

            list.Add(Make("alien", "Alien", "Space invader warble", "Al",
                3.5, 118, 0.1, 1.0, 0, 0, 0, 1.0, "none", 0, 0, false, 0, 0, 1.0));

            list.Add(Make("ghost", "Ghost", "Haunted and airy", "Gh",
                -2.0, 0, 0.45, 2.5, 0, 0, 0, 1.0, "none", 0, 0, true, 0.45, 0.6, 1.0));

            list.Add(Make("cave", "Cave", "Huge stone hall", "Ca",
                0, 0, 0.6, 4.0, 280, 0.4, 0.25, 1.0, "none", 0, 0, false, 0, 0, 1.0));

            list.Add(Make("echo", "Echo", "Slap-back repeats", "Ec",
                0, 0, 0, 0, 320, 0.5, 0.45, 1.0, "none", 0, 0, false, 0, 0, 1.0));

            list.Add(Make("radio", "Radio", "AM broadcast tone", "Ra",
                0, 0, 0, 0, 0, 0, 0, 2.4, "bandpass", 1700, 1.1, false, 0, 0, 1.15));

            list.Add(Make("phone", "Telephone", "Tiny cellphone mic", "Ph",
                0, 0, 0, 0, 0, 0, 0, 1.5, "bandpass", 1200, 1.5, false, 0, 0, 1.0));

            list.Add(Make("megaphone", "Megaphone", "Street crier shout", "Mg",
                0, 0, 0, 0, 0, 0, 0, 3.4, "bandpass", 1500, 0.9, false, 0, 0, 1.55));

            list.Add(Make("underwater", "Underwater", "Deep-sea wobble", "Uw",
                0, 0, 0.22, 2.0, 0, 0, 0, 1.0, "lowpass", 550, 1.2, true, 0.25, 0.85, 1.0));

            list.Add(Make("custom", "Custom", "Build your own mix", "Cx",
                0, 0, 0.25, 1.5, 200, 0.35, 0.3, 1.0, "none", 0, 0, false, 0, 0, 1.0));

            return list;
        }

        public static VoicePreset Find(string id)
        {
            List<VoicePreset> all = GetAll();
            for (int i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return all[i];
                }
            }
            return all[0];
        }

        /// <summary>Stable badge color per voice, used by the voice cards.</summary>
        public static System.Drawing.Color BadgeColor(string id)
        {
            switch (id)
            {
                case "helium": return System.Drawing.Color.FromArgb(0, 229, 192);
                case "chipmunk": return System.Drawing.Color.FromArgb(255, 179, 71);
                case "girl": return System.Drawing.Color.FromArgb(255, 128, 171);
                case "darth": return System.Drawing.Color.FromArgb(149, 117, 205);
                case "robot": return System.Drawing.Color.FromArgb(120, 144, 156);
                case "alien": return System.Drawing.Color.FromArgb(118, 255, 122);
                case "ghost": return System.Drawing.Color.FromArgb(176, 190, 217);
                case "cave": return System.Drawing.Color.FromArgb(141, 110, 99);
                case "echo": return System.Drawing.Color.FromArgb(79, 195, 247);
                case "radio": return System.Drawing.Color.FromArgb(255, 111, 97);
                case "phone": return System.Drawing.Color.FromArgb(129, 199, 132);
                case "megaphone": return System.Drawing.Color.FromArgb(255, 77, 141);
                case "underwater": return System.Drawing.Color.FromArgb(38, 166, 211);
                default: return System.Drawing.Color.FromArgb(0, 191, 165);
            }
        }
    }
}
