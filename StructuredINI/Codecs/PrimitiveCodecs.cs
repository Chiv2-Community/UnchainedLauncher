using System;

namespace StructuredINI.Codecs
{
    public class StringCodec : IINICodec<string>
    {
        public string Decode(string value) => value;
        public string Encode(string value) => value;
    }

    public class IntCodec : IINICodec<int>
    {
        public int Decode(string value) => int.Parse(value);
        public string Encode(int value) => value.ToString();
    }

    public class DoubleCodec : IINICodec<double>
    {
        public double Decode(string value) => double.Parse(value);
        public string Encode(double value) => value.ToString();
    }
}
