using Apilane.Common.Helpers;

namespace Apilane.UnitTests
{
    [TestClass]
    public class EitherTests
    {
        // ─── Success path ──────────────────────────────────────────────────────

        [TestMethod]
        public void Either_Success_ValueReturnsSuccess()
        {
            var either = new Either<string, int>("ok");

            Assert.AreEqual("ok", either.Value);
        }

        [TestMethod]
        public void Either_Success_IsError_ReturnsFalse()
        {
            var either = new Either<string, int>("ok");

            var isError = either.IsError(out var error);

            Assert.IsFalse(isError);
        }

        [TestMethod]
        public void Either_Success_Match_CallsSuccessFunc()
        {
            var either = new Either<string, int>("hello");

            var result = either.Match(
                s => s.ToUpper(),
                e => "error");

            Assert.AreEqual("HELLO", result);
        }

        // ─── Error path ────────────────────────────────────────────────────────

        [TestMethod]
        public void Either_Error_IsError_ReturnsTrue()
        {
            var either = new Either<string, int>(42);

            var isError = either.IsError(out var error);

            Assert.IsTrue(isError);
            Assert.AreEqual(42, error);
        }

        [TestMethod]
        public void Either_Error_Value_ThrowsInvalidOperationException()
        {
            var either = new Either<string, int>(99);

            Assert.ThrowsException<InvalidOperationException>(() => _ = either.Value);
        }

        [TestMethod]
        public void Either_Error_Match_CallsErrorFunc()
        {
            var either = new Either<string, int>(404);

            var result = either.Match(
                s => "success",
                e => $"error-{e}");

            Assert.AreEqual("error-404", result);
        }

        // ─── Void Match ────────────────────────────────────────────────────────

        [TestMethod]
        public void Either_Success_VoidMatch_CallsSuccessAction()
        {
            var either = new Either<string, int>("data");
            var called = false;

            either.Match(
                s => { called = true; },
                e => { Assert.Fail("Should not call error"); });

            Assert.IsTrue(called);
        }

        [TestMethod]
        public void Either_Error_VoidMatch_CallsErrorAction()
        {
            var either = new Either<string, int>(1);
            var called = false;

            either.Match(
                s => { Assert.Fail("Should not call success"); },
                e => { called = true; });

            Assert.IsTrue(called);
        }

        // ─── Match null guard ──────────────────────────────────────────────────

        [TestMethod]
        public void Either_Match_NullSuccessFunc_Throws()
        {
            var either = new Either<string, int>("ok");

            Assert.ThrowsException<ArgumentNullException>(() =>
                either.Match(null!, e => "error"));
        }

        [TestMethod]
        public void Either_Match_NullErrorFunc_Throws()
        {
            var either = new Either<string, int>("ok");

            Assert.ThrowsException<ArgumentNullException>(() =>
                either.Match(s => s, null!));
        }

        // ─── Implicit conversions ──────────────────────────────────────────────

        [TestMethod]
        public void Either_ImplicitFromSuccess_IsSuccess()
        {
            Either<string, int> either = "implicit-success";

            Assert.AreEqual("implicit-success", either.Value);
        }

        [TestMethod]
        public void Either_ImplicitFromError_IsError()
        {
            Either<string, int> either = 500;

            Assert.IsTrue(either.IsError(out var err));
            Assert.AreEqual(500, err);
        }
    }
}
