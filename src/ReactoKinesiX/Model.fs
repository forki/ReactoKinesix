﻿namespace ReactoKinesix.Model

open System
open Amazon.DynamoDBv2.DataModel

/// Representing the different modes in which to handle errors when processing records
type ErrorHandlingMode =
    // retry up to the specified number of times before giving up and moving on to the next record
    // NOTE : specifying zero as retry count is the same as no retry
    | RetryAndSkip      of int
    // retry up to the specified number of times before giving up and stop processing this shard
    | RetryAndStop      of int

type ReactoKinesixConfig () = 
    /// Read throughput to use for the DynamoDB table. Default is 10.
    member val DynamoDBReadThroughput  = 10L with get, set

    /// Write throughput to use for the DynamoDB table. Default is 10.
    member val DynamoDBWriteThroughput = 10L with get, set

    /// Suffix used to name your application's state table in DynamoDB. Default is "KinesisState"
    /// If the application name is "MyApp" and suffix is "KinesisState" then the DynamoDB
    /// table for this application will be called MyAppKinesisState.
    member val DynamoDBTableSuffix     = "KinesisState"
    
    /// Heartheat frequency. Default is 30 seconds.
    member val Heartbeat               = TimeSpan.FromSeconds(30.0) with get, set

    /// Timeout for the heartbeat check. Default is 3 minutes.
    member val HeartbeatTimeout        = TimeSpan.FromMinutes(3.0) with get, set

    /// Delay in trying to pull the stream if the last pull returned no records. Default is 3 second.
    member val EmptyReceiveDelay       = TimeSpan.FromSeconds(3.0) with get, set

    /// Maximum number of retries on DynamoDB operations. Default is 3.
    member val MaxDynamoDBRetries      = 3 with get, set

    /// Maximum number of retries on Kinesis operations. Default is 3.
    member val MaxKinesisRetries       = 3 with get, set

    /// How frequently should we check for shard merges/splits in the stream. Default is 1 minute.
    member val CheckStreamChangesFrequency  = TimeSpan.FromMinutes(1.0) with get, set

    /// How frequently should we check for shards whose worker has died. Default is 1 minute.
    member val CheckUnprocessedShardsFrequency = TimeSpan.FromMinutes(1.0) with get, set

[<AutoOpen>]
module Exceptions =
    /// Thrown when the configuration specifies a heartbeat frequence that's greater than the heartbeat timeout
    exception InvalidHeartbeatConfigurationException of TimeSpan * TimeSpan

    /// Thrown when the configruation for MaxDynamoDBRetries is negative
    exception NegativeMaxDynamoDBRetriesConfigurationException of int

    /// Thrown when the configruation for MaxKinesisRetries is negative
    exception NegativeMaxKinesisRetriesConfigurationException of int

    /// Thorwn when initialization of the app failed with the attached inner exception
    exception InitializationFailedException of Exception

    /// Thrown when an app with the same name 
    exception AppNameIsAlreadyRunningException of string

    /// Thrown when trying to get records from a closed shard whose records have been exhausted
    exception ShardCannotBeIteratedException

    /// Thrown when trying to initialize a shard processor but its shard cannot be found in the DynamoDB
    exception ShardNotFoundException

[<AutoOpen>]
module internal InternalModel =
    type StreamName = 
        | StreamName of string
        override this.ToString () = match this with | StreamName name -> name

    type TableName = 
        | TableName of string
        override this.ToString () = match this with | TableName name -> name

    type ShardId = 
        | ShardId of string
        override this.ToString () = match this with | ShardId id -> id

    type WorkerId = 
        | WorkerId of string
        override this.ToString () = match this with | WorkerId id -> id

    type SequenceNumber = 
        | SequenceNumber of string
        override this.ToString () = match this with | SequenceNumber seqNum -> seqNum

    type IteratorType   = 
        | TrimHorizon                               // starting at the trim horizon (i.e. earliest record available)
        | AtSequenceNumber      of SequenceNumber   // starting at the given sequence number
        | AfterSequenceNumber   of SequenceNumber   // starting immediate after the given sequence number        
        | Latest                                    // starting at the latest record
        override this.ToString () = 
            match this with
            | TrimHorizon                -> "TrimHorizon"
            | AtSequenceNumber seqNum    -> "At (" + seqNum.ToString() + ")"
            | AfterSequenceNumber seqNum -> "After (" + seqNum.ToString() + ")"
            | Latest                     -> "Latest"

    type Iterator       = 
        | IteratorToken         of string           // using the next iterator token from the previous call
        | NoIteratorToken       of IteratorType     // fetch a new iterator token
        | EndOfShard                                // the shard is closed and no more iterator can be returned
        override this.ToString () =
            match this with
            | IteratorToken token       -> "IteratorToken(" + token + ")"
            | NoIteratorToken iterType  -> iterType.ToString()
            | EndOfShard                -> "EndOfShard"
    
    type ShardStatus    = 
        | NotFound      // the shard was not found
        | Closed        // the shard was closed
        // the shard is there but not currently being processed
        | NotProcessing of WorkerId * DateTime * SequenceNumber option
        // the shard is currently being processed by a worker
        | Processing    of WorkerId * SequenceNumber option

    type Result<'Success, 'Failure> =
        | Success   of 'Success
        | Failure   of 'Failure

    type ProcessResult  = Result<SequenceNumber, SequenceNumber * Exception>
    
    type internal StoppedReason =
        | UserTriggered          = 1    // shard processor was stopped by a user
        | ShardClosed            = 2    // shard processor has stopped because its shard was closed
        | ConditionalCheckFailed = 3    // shard processor has stopped because its shard was taken over by another worker
        | ErrorInduced           = 4    // shard processor has stopped because of an error in processing records and the error handling mode is to stop
        | ProcessedByOther       = 5    // shard processor has stopped because the shard is processed by another worker

    type ControlMessage =
        | StartShardProcessor   of ShardId * AsyncReplyChannel<unit>
        | StopShardProcessor    of ShardId * AsyncReplyChannel<unit>
        | RemoveShardProcessor  of ShardId * StoppedReason
        | AddKnownShard         of ShardId * AsyncReplyChannel<unit>
        | MarkAsClosed          of ShardId * AsyncReplyChannel<unit>
        | RemoveKnownShard      of ShardId * AsyncReplyChannel<unit>