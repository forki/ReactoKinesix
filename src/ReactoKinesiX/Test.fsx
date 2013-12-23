﻿
#r "bin/Debug/AWSSDK.dll"
#r "bin/Debug/log4net.dll"
#r "bin/Debug/FSharp.Reactive.dll"
#r "bin/Debug/System.Reactive.Core.dll"
#r "bin/Debug/System.Reactive.Interfaces.dll"
#r "bin/Debug/System.Reactive.Linq.dll"
#r "bin/Debug/ReactoKinesix.dll"
//
//#load "Model.fs"
//#load "Utils.fs"
//#load "Client.fs"

open System
open System.IO
open System.Text
open Amazon
open Amazon.DynamoDBv2.DocumentModel
open Amazon.Kinesis.Model
open ReactoKinesix
open ReactoKinesix.Model
open ReactoKinesix.Utils
open log4net
open log4net.Config

let awsKey      = "AKIAI5Y767DTOFBUSYAA"
let awsSecret   = "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"
let region      = RegionEndpoint.USEast1
let streamName  = "YC-test"

BasicConfigurator.Configure()

let dynamoDB = Amazon.AWSClientFactory.CreateAmazonDynamoDBClient(awsKey, awsSecret, region) 
let kinesis = Amazon.AWSClientFactory.CreateAmazonKinesisClient(awsKey, awsSecret, region) 

let putRecord (payload : string) =
    let req  = new PutRecordRequest(StreamName = streamName, PartitionKey = Guid.NewGuid().ToString())
    req.Data <- new MemoryStream(Encoding.UTF8.GetBytes(payload))
    kinesis.PutRecord(req) |> ignore

let act (record : Record) =
    use streamReader = new StreamReader(record.Data)
    printfn "\n\n\n\n\n\n\n\n\n\n%s : %s\n\n\n\n\n\n\n\n\n\n" record.SequenceNumber <| streamReader.ReadToEnd()

let act2 (record : Record) =
    use streamReader = new StreamReader(record.Data)
    printfn """

=================================================
=================================================
=================================================
%s : %s
=================================================
=================================================
=================================================

"""         record.SequenceNumber <| streamReader.ReadToEnd()

let processor = { new IRecordProcessor with member this.Process record = act record }
let processor2 = { new IRecordProcessor with member this.Process record = act2 record }

let app = ReactoKinesixApp.CreateNew(awsKey, awsSecret,region, "YC-test", streamName, "PHANTOM", processor)

app.StartProcessing("shardId-000000000002")
app.StopProcessing("shardId-000000000002")
app.ChangeProcessor(processor2)

//{ 1..100 } |> Seq.iter (fun i -> putRecord <| i.ToString())

(app :> IDisposable).Dispose()

