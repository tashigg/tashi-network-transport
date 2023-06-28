#nullable enable

using System;

namespace Tashi.ConsensusEngine
{
    enum Result
    {
        Success,
        LogFilePathError,
        ParseAddressError,
        BindListenerError,
        NotInited,
        NotStarted,
        RuntimeCreationError,
        SecretKeyConstructionError,
        PlatformCreationError,
        BufferTooSmall,
        ConversionToDerError,
        ConversionFromDerError,
        SpawnUdpError,
        SendError,
        DataTooLarge,
        CreatorIdTooLarge,
        CreatorIdMissing,
        ReceiveEventError,
        InitialNodesMissing,
        AlreadyStarted,
        EmptyAddressBook,
        FailedToDetermineLocalIp,
        TransmitQueueEmpty,
        ArgumentError,
        ExternalModeRequired,
        RecvNotReady,
        InvalidSockAddr,
        BindAddressRequired,
        LoggingAlreadySet,
        RelaySessionError,
        RelaySessionInitIncomplete,
        RelaySessionRateLimited,
        RelaySessionUserLimitExceeded,
        RelaySessionIntervalOutOfRange,
    }

    static class ResultExtensions
    {
        public static void SuccessOrThrow(this Result result, string operationName)
        {
            if (result != Result.Success)
            {
                throw new ResultException(result, operationName);
            }
        }
    }

    sealed class ResultException : Exception
    {
        internal ResultException(Result result, string operationName) : base($"error from {operationName}: {result}")
        {
        }
    }
}