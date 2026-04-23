using FluentAssertions;
using QtoRevitPlugin.AI;
using System;
using Xunit;

namespace QtoRevitPlugin.Tests.AI
{
    public class EmbeddingSerializerTests
    {
        [Fact]
        public void Serialize_ReturnsBytesLengthEqualToFloatsTimesFour()
        {
            var vec = new float[] { 1f, 2f, 3f, 4f };
            var bytes = EmbeddingSerializer.Serialize(vec);
            bytes.Length.Should().Be(vec.Length * sizeof(float));
        }

        [Fact]
        public void Roundtrip_PreservesValues()
        {
            var original = new float[] { 0.1f, -0.5f, 0f, 1e-6f, -1e6f, float.Epsilon };
            var blob = EmbeddingSerializer.Serialize(original);
            var roundtrip = EmbeddingSerializer.Deserialize(blob);

            roundtrip.Should().BeEquivalentTo(original);
        }

        [Fact]
        public void Roundtrip_TypicalEmbeddingSize_768Dim()
        {
            // nomic-embed-text produce vettori 768-dim
            var original = new float[768];
            var rng = new Random(42);
            for (int i = 0; i < original.Length; i++)
                original[i] = (float)(rng.NextDouble() * 2 - 1); // [-1, 1]

            var roundtrip = EmbeddingSerializer.Deserialize(EmbeddingSerializer.Serialize(original));

            roundtrip.Length.Should().Be(768);
            for (int i = 0; i < original.Length; i++)
                roundtrip[i].Should().Be(original[i]);
        }

        [Fact]
        public void Deserialize_InvalidLength_Throws()
        {
            var invalidBlob = new byte[7]; // 7 non è multiplo di 4
            var act = () => EmbeddingSerializer.Deserialize(invalidBlob);
            act.Should().Throw<ArgumentException>()
               .WithMessage("*non multipla*");
        }

        [Fact]
        public void Serialize_NullInput_Throws()
        {
            var act = () => EmbeddingSerializer.Serialize(null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Deserialize_NullInput_Throws()
        {
            var act = () => EmbeddingSerializer.Deserialize(null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void EmptyVector_RoundtripsToEmptyArray()
        {
            var empty = Array.Empty<float>();
            var blob = EmbeddingSerializer.Serialize(empty);
            blob.Length.Should().Be(0);

            var roundtrip = EmbeddingSerializer.Deserialize(blob);
            roundtrip.Should().BeEmpty();
        }
    }
}
