using System.Collections.Generic;
using FluentAssertions;
using QtoRevitPlugin.Formula;
using Xunit;

namespace QtoRevitPlugin.Tests.Formula
{
    /// <summary>
    /// Test per <see cref="FormulaEngine"/> — copertura sintassi, parametri, errori runtime.
    /// Usa <see cref="FakeParameterResolver"/> dictionary-based per disaccoppiare dai Revit API.
    /// </summary>
    public class FormulaEngineTests
    {
        private sealed class FakeParameterResolver : IParameterResolver
        {
            // Case-insensitive: match con EvaluateOptions.IgnoreCase in FormulaEngine.
            private readonly Dictionary<string, double> _values;

            public FakeParameterResolver(Dictionary<string, double>? values = null)
            {
                _values = new Dictionary<string, double>(
                    values ?? new Dictionary<string, double>(),
                    System.StringComparer.OrdinalIgnoreCase);
            }

            public double? TryResolve(string parameterName)
                => _values.TryGetValue(parameterName, out var v) ? v : (double?)null;
        }

        // -----------------------------------------------------------------
        // Evaluate — letterali numerici
        // -----------------------------------------------------------------

        [Fact]
        public void Evaluate_SimpleLiteral_ReturnsNumber()
        {
            var result = new FormulaEngine().Evaluate("12.5 * 2", new FakeParameterResolver());

            result.IsValid.Should().BeTrue();
            result.Error.Should().BeNull();
            result.Value.Should().BeApproximately(25.0, 0.0001);
            result.UnresolvedIds.Should().BeEmpty();
        }

        [Fact]
        public void Evaluate_DecimalDotInvariantCulture_Works()
        {
            // NCalc è cultura-invariante: "12.5" è sempre 12.5 anche su Windows italiano.
            var result = new FormulaEngine().Evaluate("12.5 + 0.5", new FakeParameterResolver());

            result.IsValid.Should().BeTrue();
            result.Value.Should().BeApproximately(13.0, 0.0001);
        }

        // -----------------------------------------------------------------
        // Evaluate — parametri via resolver
        // -----------------------------------------------------------------

        [Fact]
        public void Evaluate_WithParameter_ResolvesViaResolver()
        {
            var resolver = new FakeParameterResolver(new Dictionary<string, double>
            {
                ["Area"] = 100.0
            });

            var result = new FormulaEngine().Evaluate("Area * 1.08", resolver);

            result.IsValid.Should().BeTrue();
            result.Value.Should().BeApproximately(108.0, 0.0001);
            result.UnresolvedIds.Should().BeEmpty();
        }

        [Fact]
        public void Evaluate_MultipleParameters_AllResolved()
        {
            var resolver = new FakeParameterResolver(new Dictionary<string, double>
            {
                ["Perimeter"] = 20.0,
                ["H_Controsoffitto"] = 2.70
            });

            var result = new FormulaEngine().Evaluate("Perimeter * H_Controsoffitto", resolver);

            result.IsValid.Should().BeTrue();
            result.Value.Should().BeApproximately(54.0, 0.0001);
        }

        [Fact]
        public void Evaluate_IdentifiersCaseInsensitive()
        {
            // Verifica policy: FormulaEngine usa EvaluateOptions.IgnoreCase di NCalc.
            var resolver = new FakeParameterResolver(new Dictionary<string, double>
            {
                ["Area"] = 50.0
            });

            var upper = new FormulaEngine().Evaluate("AREA * 2", resolver);
            var lower = new FormulaEngine().Evaluate("area * 2", resolver);
            var mixed = new FormulaEngine().Evaluate("Area * 2", resolver);

            upper.IsValid.Should().BeTrue();
            lower.IsValid.Should().BeTrue();
            mixed.IsValid.Should().BeTrue();
            upper.Value.Should().BeApproximately(100.0, 0.0001);
            lower.Value.Should().BeApproximately(100.0, 0.0001);
            mixed.Value.Should().BeApproximately(100.0, 0.0001);
        }

        // -----------------------------------------------------------------
        // Evaluate — parametri non risolti
        // -----------------------------------------------------------------

        [Fact]
        public void Evaluate_UnresolvedParameter_TracksIdAndContinues()
        {
            var resolver = new FakeParameterResolver(new Dictionary<string, double>
            {
                ["Area"] = 100.0
            });

            // Pippo non è nel resolver: deve essere sostituito con 0 → Area * 0 = 0, ma formula è valida.
            var result = new FormulaEngine().Evaluate("Area * Pippo", resolver);

            result.IsValid.Should().BeTrue();
            result.UnresolvedIds.Should().Contain("Pippo");
            result.UnresolvedIds.Should().HaveCount(1);
            result.Value.Should().Be(0.0);
        }

        [Fact]
        public void Evaluate_UnresolvedParameter_DedupsInListEvenIfReferencedTwice()
        {
            var resolver = new FakeParameterResolver();

            var result = new FormulaEngine().Evaluate("X + X + X", resolver);

            result.IsValid.Should().BeTrue();
            result.UnresolvedIds.Should().HaveCount(1);
            result.UnresolvedIds.Should().Contain("X");
        }

        // -----------------------------------------------------------------
        // Evaluate — errori di sintassi
        // -----------------------------------------------------------------

        [Fact]
        public void Evaluate_SyntaxError_ReturnsInvalid()
        {
            var result = new FormulaEngine().Evaluate("Area *", new FakeParameterResolver());

            result.IsValid.Should().BeFalse();
            result.Error.Should().NotBeNullOrEmpty();
            result.Value.Should().Be(0.0);
        }

        [Fact]
        public void Evaluate_EmptyFormula_ReturnsInvalid()
        {
            var result = new FormulaEngine().Evaluate("", new FakeParameterResolver());

            result.IsValid.Should().BeFalse();
            result.Error.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void Evaluate_WhitespaceFormula_ReturnsInvalid()
        {
            var result = new FormulaEngine().Evaluate("   ", new FakeParameterResolver());

            result.IsValid.Should().BeFalse();
            result.Error.Should().NotBeNullOrEmpty();
        }

        // -----------------------------------------------------------------
        // Evaluate — divisione per zero
        // -----------------------------------------------------------------

        [Fact]
        public void Evaluate_DivisionByZero_IsHandledGracefully()
        {
            // NCalc2 può throw DivideByZeroException o produrre Infinity a seconda della versione.
            // In entrambi i casi il FormulaEngine NON deve propagare il throw.
            var result = new FormulaEngine().Evaluate("10 / 0", new FakeParameterResolver());

            // Comportamento documentato: o IsValid=false (con Error), oppure IsValid=true con Value=Infinity.
            // In entrambi i casi il chiamante può rilevarlo e non crasha.
            if (result.IsValid)
            {
                double.IsInfinity(result.Value).Should().BeTrue(
                    "se NCalc restituisce un valore numerico, deve essere Infinity (non 0)");
            }
            else
            {
                result.Error.Should().NotBeNullOrEmpty();
            }
        }

        // -----------------------------------------------------------------
        // Evaluate — funzioni NCalc built-in
        // -----------------------------------------------------------------

        [Fact]
        public void Evaluate_MaxFunction_WorksWithResolver()
        {
            var resolver = new FakeParameterResolver(new Dictionary<string, double>
            {
                ["Area"] = 100.0,
                ["Perimeter"] = 40.0
            });

            var result = new FormulaEngine().Evaluate("Max(Area, Perimeter)", resolver);

            result.IsValid.Should().BeTrue();
            result.Value.Should().BeApproximately(100.0, 0.0001);
        }

        [Fact]
        public void Evaluate_AbsFunction_Works()
        {
            var result = new FormulaEngine().Evaluate("Abs(-5.5)", new FakeParameterResolver());

            result.IsValid.Should().BeTrue();
            result.Value.Should().BeApproximately(5.5, 0.0001);
        }

        // -----------------------------------------------------------------
        // Validate — controllo sintassi standalone
        // -----------------------------------------------------------------

        [Fact]
        public void Validate_ValidFormula_ReturnsTrue()
        {
            var engine = new FormulaEngine();

            var ok = engine.Validate("Area * 1.08", out var error);

            ok.Should().BeTrue();
            error.Should().BeEmpty();
        }

        [Fact]
        public void Validate_SyntaxError_ReturnsFalseWithError()
        {
            var engine = new FormulaEngine();

            var ok = engine.Validate("Area *", out var error);

            ok.Should().BeFalse();
            error.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void Validate_EmptyFormula_ReturnsFalse()
        {
            var engine = new FormulaEngine();

            var ok = engine.Validate("", out var error);

            ok.Should().BeFalse();
            error.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void Validate_UnknownIdentifier_StillValidSyntactically()
        {
            // Validate guarda solo la sintassi: identificatori sconosciuti sono leciti a questo stadio.
            var engine = new FormulaEngine();

            var ok = engine.Validate("UnknownParam + 1", out var error);

            ok.Should().BeTrue();
            error.Should().BeEmpty();
        }
    }
}
