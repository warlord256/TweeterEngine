module Simulator

open System
open Akka.FSharp
open Akka.Actor
open System.Collections.Generic
open System.Diagnostics
open Types
open System.IO
//open TweeterEngine

type WorkerMessages = 
    | Initiate of string*int
    | InitiateSubscription
    | SubscribeTo of string
    | CreateTweet
    | QueryTweets
    | Shutdown

type Stats = {
    UserId: int
    TweetCount: int64
    TweetProbablity: float
    SubCount: int
    QueriesCount: int64
    QuerySubCount: int64
    QueryHashTagCount: int64
    QueryMentionsCount: int64
    RetweetCount: int64
    AvgQuerySubTime: int64
    AvgQueryHashtagsTime: int64
    AvgQueryMentionsTime: int64   
}

type MasterMessages = 
    | InitiateSimulation of int*int
    | IndividualStats of Stats
    | PrintAllStats
    | EndSimulation

let system = ActorSystem.Create("ActorFactory")
let mutable engine: IActorRef =  null //spawn system "tweeter" tweeterEngine // 
let rand = Random()
let usernames = new List<string>()
let hashTagsList = new List<string>()

let randomString = 
    let r = Random()
    let chars = Array.concat([[|'a' .. 'z'|];[|'A' .. 'Z'|];[|'0' .. '9'|];])
    fun n ->
        let sz = Array.length chars in
        String(Array.init n (fun _ -> chars.[r.Next sz]))

let generateTweet() =
    let wordList = List.init (rand.Next(1,10)) (fun x -> randomString (rand.Next(1,10)))
    let mutable tweet = String.Join(" ", wordList)
    let hashTags = List.init (rand.Next(1,3)) (fun x -> hashTagsList.Item(rand.Next(hashTagsList.Count)))
    tweet <- tweet + " " + String.Join(" ", hashTags)
    if rand.Next(10) > 8 then
        for i in 1..rand.Next(1,3) do
            tweet <- tweet + " @" + usernames.Item(rand.Next(usernames.Count))
    tweet 

let checkForRetweet (tweets: List<Tweet>, userId:int) =
    if tweets.Count > 0 && rand.Next(10) < 1 then
        let mutable toRetweet = tweets.Item(rand.Next(tweets.Count))
        let mutable limit = 3
        while toRetweet.By=userId && limit > 0 do
            toRetweet <- tweets.Item(rand.Next(tweets.Count))
            limit <- limit-1
        if toRetweet.By<>userId then
            engine <! SendRetweet(userId, toRetweet.Id)
            true
        else 
            false
    else
        false

let workerActor(mailbox:Actor<_>) = 
    let mutable userId = -1
    let mutable subscriberCount = 0
    let mutable tweetProbablity = 0.0
    let mutable tweetCount = 0L
    let mutable queriesCount = 0L
    let mutable querySubTimeCount = 0L
    let mutable queryHashTagCount = 0L
    let mutable queryMentionsCount = 0L
    let mutable retweetCount = 0L
    let mutable avgQuerySubTime = 0L
    let mutable avgQueryHashtagsTime = 0L
    let mutable avgQueryMentionsTime = 0L
    let mutable username = ""
    let toSub = new HashSet<string>()
    let clock = Stopwatch()
    let hashClock = Stopwatch()
    let mentionsClock = Stopwatch() 
    let mutable cancellationToken1:ICancelable = null        
    let mutable cancellationToken2:ICancelable = null
    let mutable lastReadTweet = 0
    let mutable queryToRun = 0
    let rec loop() = actor {
        let! msg = mailbox.Receive() 
        match box msg with
        | :? WorkerMessages as message ->
            match message with  
                | Initiate(name, noOfSub) ->
                    clock.Start()
                    username <- name
                    engine <! Register(name)   
                    subscriberCount <- noOfSub
                | InitiateSubscription ->
                    let subCount = subscriberCount
                    let subs = new HashSet<string>()
                    while subs.Count < subCount do
                        subs.Add(usernames.Item(rand.Next(usernames.Count))) |> ignore
                    for user in subs do
                        let index = (usernames.FindIndex(fun x -> x.Equals(user))+1)
                        let node = mailbox.Context.ActorSelection("akka://ActorFactory/user/master/"+ (index.ToString()))
                        node <! SubscribeTo(username)
                | SubscribeTo(from) -> 
                    if userId <> -1 then
                        let toSend = new List<string>(toSub)
                        toSend.Add(from)
                        engine <! Subscribe(userId, toSend)
                        toSub.Clear()
                    else 
                        toSub.Add(from) |> ignore
                | CreateTweet ->
                    if rand.Next(100) < int (tweetProbablity*100.0) then
                        engine <! SendTweet(userId, generateTweet())
                        tweetCount <- tweetCount+1L
                | QueryTweets ->
                    queriesCount <- queriesCount + 1L
                    queryToRun <- queryToRun + 1
                    match queryToRun with
                        | 1 | 3->
                            querySubTimeCount <-  querySubTimeCount + 1L
                            clock.Start()
                            engine <! QuerySubscriptions(userId, lastReadTweet)  
                        | 2->
                            queryHashTagCount <- queryHashTagCount + 1L 
                            let hashTags = new List<string>()
                            hashTags.Add( hashTagsList.Item(rand.Next(hashTagsList.Count)))
                            hashClock.Start()
                            engine <! QueryHashtags(hashTags)
                        | _ ->                            
                            queryMentionsCount <- queryMentionsCount + 1L
                            mentionsClock.Start()
                            engine <! QueryMentions(userId)
                            queryToRun <- 0 
                | Shutdown ->
                    let stat =  {
                        UserId = userId
                        TweetCount = tweetCount
                        TweetProbablity = tweetProbablity
                        SubCount = subscriberCount
                        QueriesCount = queriesCount
                        QuerySubCount = querySubTimeCount
                        QueryHashTagCount = queryHashTagCount
                        QueryMentionsCount = queryMentionsCount
                        RetweetCount = retweetCount
                        AvgQuerySubTime = avgQuerySubTime/(if querySubTimeCount = 0L then 1L else querySubTimeCount)
                        AvgQueryHashtagsTime = avgQueryHashtagsTime/(if queryHashTagCount = 0L then 1L else queryHashTagCount)
                        AvgQueryMentionsTime = avgQueryMentionsTime /(if queryMentionsCount = 0L then 1L else queryMentionsCount)
                     }
                    engine <! Logout(userId)
                    mailbox.Sender() <! IndividualStats(stat)
                    cancellationToken1.Cancel()
                    cancellationToken2.Cancel()
                    //mailbox.Self <! PoisonPill.Instance
            return! loop()
        | :? Responses as message ->
            match message with 
                | RegistrationSuccess(id) -> 
                    clock.Stop()
                    userId<- id
                    engine <! Login(username, userId)
                    cancellationToken1 <- system.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.FromSeconds(0.0), TimeSpan.FromSeconds(0.5), mailbox.Self, CreateTweet, mailbox.Self)
                    cancellationToken2 <- system.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.FromSeconds(0.0), TimeSpan.FromSeconds(1.0), mailbox.Self, QueryTweets, mailbox.Self)
                    tweetProbablity <- ((float subscriberCount)/(float usernames.Count))
                    // if tweetProbablity<0.1 then
                    //     tweetProbablity <- 0.1 //((float (rand.Next(2)))/10.0)
                    mailbox.Self <! InitiateSubscription
                | QueryResponses (tweets,queryType) ->
                    if queryType = 'S' then
                        clock.Stop()
                        let mutable maxId = 0
                        for tweet in tweets do
                            maxId <- max maxId tweet.Id
                        lastReadTweet <- maxId
                        if checkForRetweet(tweets, userId) then
                            retweetCount <- retweetCount + 1L
                        avgQuerySubTime <- if avgQuerySubTime=0L then clock.ElapsedMilliseconds else (avgQuerySubTime+clock.ElapsedMilliseconds)
                    elif queryType = 'H' then
                        hashClock.Stop()
                        if checkForRetweet(tweets, userId) then
                            retweetCount <- retweetCount + 1L
                        avgQueryHashtagsTime <- if avgQueryHashtagsTime=0L then clock.ElapsedMilliseconds else (avgQueryHashtagsTime+clock.ElapsedMilliseconds)
                    else
                        mentionsClock.Stop()
                        if checkForRetweet(tweets, userId) then
                            retweetCount <- retweetCount + 1L 
                        avgQueryMentionsTime <- if avgQueryMentionsTime=0L then clock.ElapsedMilliseconds else (avgQueryMentionsTime+clock.ElapsedMilliseconds)
                | _ -> printfn ""                   
        | _ -> printfn "Unkown worker message type"
        return! loop()
    }
    loop()

let init (numberOfWorkers: int, workers: List<IActorRef>, mailbox:Actor<_>) = 
    for i in 1..numberOfWorkers do
        workers.Add(spawn mailbox.Context (string i) workerActor)

    let subCounts = ZipfGen.generateList(numberOfWorkers)
    let mutable index = 0
    
    for node in workers do
        let username = randomString (rand.Next(3,10))
        usernames.Add(username)
        node <! Initiate(username, subCounts.[index])
        index <- index + 1

    for i in 0..rand.Next(100,200) do
        hashTagsList.Add("#"+ randomString (rand.Next(1,10)))

let collectStats (workers: List<IActorRef>) =
    for node in workers do
        node <! Shutdown
    engine <! GetStats

let masterActor(mailbox: Actor<_>) = 
    let workers = new List<IActorRef> ()    
    let overAllClock = Stopwatch()
    let mutable initiator:IActorRef = null
    let mutable indStats = 0
    let mutable subsVsFreq = new Dictionary<int,int>()
    let mutable indTweetCount = new Dictionary<int,int64*int>()
    let mutable receivedEngStats = false               
    let rec loop() = actor {
        let! msg = mailbox.Receive()
        match box msg with
        | :? MasterMessages as message ->
            match message with
                | InitiateSimulation(noOfWorkers, durationOfSimulation) ->
                    overAllClock.Start()
                    init(noOfWorkers, workers, mailbox)
                    Async.RunSynchronously <| Async.Sleep(durationOfSimulation)
                    File.WriteAllText(@".\Ind_Stat.txt", "Individual Stats:\n")
                    collectStats(workers)
                    initiator <- mailbox.Sender()
                | IndividualStats(stats) ->
                    indTweetCount.Add(stats.UserId, (stats.TweetCount,stats.SubCount))
                    if not (subsVsFreq.ContainsKey(stats.SubCount)) then
                            subsVsFreq.Add(stats.SubCount,0)
                    subsVsFreq.Item(stats.SubCount) <-  subsVsFreq.Item(stats.SubCount) + 1
                    indStats <- indStats + 1
                    let str = stats.ToString() + "\n"
                    File.AppendAllText(@".\Ind_Stat.txt",str)
                    if indStats = workers.Count && receivedEngStats then
                        mailbox.Self <! PrintAllStats
                | PrintAllStats ->
                    File.WriteAllText(@".\TweetCountVsSubCount.txt", "TweetCount| SubCount\n")
                    for (a,b) in indTweetCount.Values do    
                        let str = string a + " " + string b + "\n"
                        File.AppendAllText(@".\TweetCountVsSubCount.txt",str)
                    File.WriteAllText(@".\SubCountVsFrequency.txt", "SubCount | Frequency\n")
                    for itr in subsVsFreq do
                        let str = string itr.Key + " " + string itr.Value + "\n"
                        File.AppendAllText(@".\SubCountVsFrequency.txt",str)
                    initiator <! "Done"
                | _ -> printfn ""
            return! loop()
        | :? Responses as message ->   
            match message with  
                | OverallStats(engineStats) ->
                    receivedEngStats <- true
                    overAllClock.Stop()                    
                    printfn " Overall Stats %A" engineStats
                    let timeTaken = overAllClock.ElapsedMilliseconds / int64 1000
                    printfn "TimeTaken %d" overAllClock.ElapsedMilliseconds
                    let avgRequestsPerSec = int64 engineStats.RequestsReceived / timeTaken
                    printfn " Requests processed per second %i" avgRequestsPerSec                    
                    if indStats = workers.Count && receivedEngStats then
                        mailbox.Self <! PrintAllStats
                | _ -> printfn "Don't know that one."                    
        | _ -> printfn "Unkown message type"
        return! loop()
    }
    loop()

// [<EntryPoint>]
// let main argv =
//     let master = spawn system "master" masterActor
//     let useless = Async.RunSynchronously <| (master <? InitiateSimulation(100)) 
//     system.Terminate() |> ignore
//     0

