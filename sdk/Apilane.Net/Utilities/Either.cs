using System;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Apilane.Net.Utilities
{
    public class Either<TSuccess, TError>
    {
        private readonly TSuccess _success;
        private readonly TError _error;
        private readonly bool _isSuccess;

        public TSuccess Value
        {
            get
            {
                if (!_isSuccess)
                {
                    throw new InvalidOperationException($"Requested invalid value of type {typeof(TSuccess).Name}");
                }

                return _success;
            }
        }

        public bool HasError(out TError error)
        {
            error = _error;
            return !_isSuccess;
        }

        public Either(TSuccess success)
        {
            this._success = success;
            this._isSuccess = true;
        }

        public Either(TError error)
        {
            this._error = error;
            this._isSuccess = false;
        }

        public T Match<T>(Func<TSuccess, T> successFunc, Func<TError, T> errorFunc)
        {
            if (successFunc == null)
            {
                throw new ArgumentNullException(nameof(successFunc));
            }

            if (errorFunc == null)
            {
                throw new ArgumentNullException(nameof(errorFunc));
            }

            return this._isSuccess ? successFunc(this._success) : errorFunc(this._error);
        }

        public void Match(Action<TSuccess> successFunc, Action<TError> errorFunc)
        {
            if (successFunc == null)
            {
                throw new ArgumentNullException(nameof(successFunc));
            }

            if (errorFunc == null)
            {
                throw new ArgumentNullException(nameof(errorFunc));
            }

            if (this._isSuccess)
            {
                successFunc(this._success);
            }
            else
            {
                errorFunc(this._error);
            };
        }

        public static implicit operator Either<TSuccess, TError>(TSuccess success) => new Either<TSuccess, TError>(success);

        public static implicit operator Either<TSuccess, TError>(TError error) => new Either<TSuccess, TError>(error);
    }
}

#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.