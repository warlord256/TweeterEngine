
#time
#r "nuget: Akka.FSharp"
#r "nuget: Akka.Remote"
#r "nuget: Akka.Serialization.Hyperion"

#load "Types.fs"
#load "ZipfGen.fs"
#load "Simulator.fs"

open System
open Akka.FSharp
open Akka.Actor
open Simulator
     
let config =  
    Configuration.parse
        @"akka {
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                serializers {
                      wire = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                }
                    serialization-bindings {
                  ""System.Object"" = wire
                }
            }
            remote {
                maximum-payload-bytes = 30000000 bytes
                helios.tcp {
                    message-frame-size =  30000000b
                    send-buffer-size =  30000000b
                    receive-buffer-size =  30000000b
                    maximum-frame-size = 30000000b
                }
            }        
               
            
        }"

let client = "127.0.0.1"

let system = ActorSystem.Create("ActorFactory", config)

// engine <- (spawne system "engine" <@ TweeterEngine.tweeterEngine @> [SpawnOption.Deploy(Deploy(RemoteScope (Address.Parse "akka.tcp://RemoteActorFactory@192.168.0.100:9001")))])

engine <- (Async.RunSynchronously <| Async.AwaitTask(system.ActorSelection("akka.tcp://RemoteActorFactory@"+client+":9001/user/engine").ResolveOne(TimeSpan.FromSeconds(3.0))))

let master = spawn system "master" masterActor

let noOfNodes = if fsi.CommandLineArgs.Length > 1 then int (fsi.CommandLineArgs.[1] ) else 1000
let duration = if fsi.CommandLineArgs.Length > 2 then int (fsi.CommandLineArgs.[2] ) else 50000

let useless:string = Async.RunSynchronously <| (master <? InitiateSimulation(noOfNodes, duration)) 

system.Terminate()
