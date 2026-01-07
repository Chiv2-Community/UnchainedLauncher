using System.Globalization;

namespace StructuredINI.Codecs {
    public class StringCodec : IINICodec<string> {
        public string Decode(string value) => value;
        public string Encode(string value) => value;
    }
    
    public class BoolCodec : IINICodec<bool> {
        public bool Decode(string value) => bool.Parse(value);
        public string Encode(bool value) => value.ToString();
    }

    public class IntCodec : IINICodec<int> {
        public int Decode(string value) => int.Parse(value);
        public string Encode(int value) => value.ToString();
    }

    public class DoubleCodec : IINICodec<double> {
        public double Decode(string value) => double.Parse(value, CultureInfo.InvariantCulture);
        public string Encode(double value) => value.ToString(CultureInfo.InvariantCulture);
    }

    public class FloatCodec : IINICodec<float> {
        public float Decode(string value) => float.Parse(value, CultureInfo.InvariantCulture);
        public string Encode(float value) => value.ToString(CultureInfo.InvariantCulture);
    }
}