﻿module Samples.Store.Integration.LogIntegration

open Swensen.Unquote

#nowarn "1182" // From hereon in, we may have some 'unused' privates (the tests)

let batchSize = 500
let createCartServiceWithEventStore eventStoreConnection =
    let gateway = createGesGateway eventStoreConnection batchSize
    Backend.Cart.Service(createGesStream gateway)

let createLoggerWithCapture emit =
    let capture = SerilogTracerAdapter emit
    let subscribeLogListeners obs =
        obs |> capture.Subscribe |> ignore
    createLogger subscribeLogListeners, capture

type Tests() =
    [<AutoData>]
    let ``Can roundtrip against EventStore, with control over logging, correctly batching the reads and folding the events`` context cartId skuId = Async.RunSynchronously <| async {
        let! conn = connectToLocalEventStoreNode ()
        let buffer = ResizeArray<string>()
        let emit msg = System.Diagnostics.Trace.WriteLine msg; buffer.Add msg
        let (log,capture), service = createLoggerWithCapture emit, createCartServiceWithEventStore conn

        let itemCount = batchSize / 2 + 1
        do! CartIntegration.addAndThenRemoveItemsManyTimesExceptTheLastOne context cartId skuId log service itemCount

        let! state = service.Load log cartId
        test <@ itemCount = match state with { items = [{ quantity = quantity }] } -> quantity | _ -> failwith "nope" @>

        // Because we've gone over a page, we need two reads to load the state, making a total of three
        let contains (s : string) (x : string) = x.IndexOf s <> -1
        test <@ let reads = buffer |> Seq.filter (fun s -> s |> contains "ReadStreamEventsForwardAsync-Elapsed")
                3 <= Seq.length reads
                && not (obj.ReferenceEquals(capture, null)) @>
    }