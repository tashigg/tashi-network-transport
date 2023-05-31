using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Tashi.ConsensusEngine
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeConsensusEvent
    {
        // This is an array of variable length arrays. Each transaction has a
        // 4 byte length prefix.
        internal readonly IntPtr packed_transactions;
        internal readonly UInt32 packed_transactions_len;
        internal readonly UInt64 timestamp_created;
        internal readonly UInt64 timestamp_received;
        internal readonly IntPtr creator_id;
        internal readonly UInt32 creator_id_len;
    }

    public class ConsensusEvent
    {
        public ulong TimestampCreated { get; }
        public ulong TimestampReceived { get; }
        public PublicKey CreatorPublicKey { get; }

        public List<ArraySegment<byte>> Transactions { get; } = new();

        internal ConsensusEvent(NativeConsensusEvent fce)
        {
            switch (fce.packed_transactions_len)
            {
                case > Int32.MaxValue:
                    throw new ArgumentOutOfRangeException(nameof(fce));
                case > 0:
                {
                    var packedTransactions = new byte[fce.packed_transactions_len];
                    Marshal.Copy(fce.packed_transactions, packedTransactions, 0, (int)fce.packed_transactions_len);
                    var index = 0;
                    while (index < fce.packed_transactions_len)
                    {
                        var length = BitConverter.ToInt32(packedTransactions, index);
                        index += 4;

                        Transactions.Add(new ArraySegment<byte>(packedTransactions, index, length));
                        index += length;
                    }

                    break;
                }
            }

            if (fce.creator_id_len == 0)
            {
                throw new Exception("The event doesn't have a creator");
            }

            var der = new byte[fce.creator_id_len];
            Marshal.Copy(fce.creator_id, der, 0, (int)fce.creator_id_len);
            CreatorPublicKey = new PublicKey(der);

            TimestampCreated = fce.timestamp_created;
            TimestampReceived = fce.timestamp_received;
        }
    }
}