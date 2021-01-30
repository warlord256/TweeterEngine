
#time
#r "nuget: Akka.FSharp"
#r "nuget: Akka.Remote"
#r "nuget: Akka.Serialization.Hyperion"

#load "Types.fs"
#load "Program.fs"

open System
open Akka.FSharp
open Akka.Actor
open TweeterEngine

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
                    hostname = 127.0.0.1
                    port = 9001
                    message-frame-size =  30000000b
                    send-buffer-size =  30000000b
                    receive-buffer-size =  30000000b
                    maximum-frame-size = 30000000b
                    tcp-reuse-addr = off
                }
            }
        }"


     
let system = ActorSystem.Create("RemoteActorFactory", config)

let engine = spawne system "engine" <@ tweeterEngine @> []

printfn "Waiting for connection"

Console.ReadKey()
