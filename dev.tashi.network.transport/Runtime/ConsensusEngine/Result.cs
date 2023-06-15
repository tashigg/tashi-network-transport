#nullable enable

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
    }
}