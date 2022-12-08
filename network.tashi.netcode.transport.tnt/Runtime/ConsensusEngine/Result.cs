#nullable enable

namespace Tashi.ConsensusEngine
{
    enum Result
    {
        Success,
        LogFilePath,
        ParseAddress,
        BindListener,
        NotInitialized,
        NotStarted,
        RuntimeCreationFailure,
        SecretKeyConstruction,
        PlatformCreation,
        BufferTooSmall,
        ConversionToDer,
        ConversionFromDer,
        SpawnUdp,
        SendFailed,
        DataTooLarge,
        CreatorIdTooLarge,
        CreatorIdMissing,
        ReceiveEventError,
        InitialNodesMissing,
        AlreadyStarted,
        EmptyAddressBook,
        FailedToDetermineLocalIp,
    }
}