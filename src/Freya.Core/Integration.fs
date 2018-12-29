namespace Freya.Core

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging

#if TASKS

open FSharp.Control.Tasks.V2
// open FSharp.Control.Tasks.V2.ContextInsensitive

#endif

#if HOPAC

open Hopac
open Hopac.Extensions

#endif

// Integration

// Utility functionality for integrating the Freya model of computation with
// wider standards, in this case generally OWIN compatible servers and tools
// through the use of adapter functions from specification signatures to
// Freya signatures and vice versa.

#if TASKS
type HttpFuncResult = Task<HttpContext option>
#else
#if HOPAC
type HttpFuncResult = Job<HttpContext option>
#else
type HttpFuncResult = Async<HttpContext option>
#endif
#endif

type HttpFunc = HttpContext -> HttpFuncResult

type HttpHandler = HttpFunc -> HttpFunc

type FreyaMiddleware (next:RequestDelegate, handler:HttpHandler, loggerFactory:ILoggerFactory) =
    do if isNull next then raise (ArgumentNullException("next"))

    // pre-compile the handler pipeline
    let func : HttpFunc =
#if TASKS
        handler (fun ctx -> Some ctx |> Task.FromResult)
#else
#if HOPAC
        handler (fun ctx -> Some ctx |> Job.result)
#else
        handler (fun ctx -> Some ctx |> async.Return)
#endif
#endif

    member __.Invoke (ctx : HttpContext) =
#if TASKS
        task {
#else
#if HOPAC
        job {
#else
        async {
#endif
#endif
            let start = System.Diagnostics.Stopwatch.GetTimestamp();

            let! result = func ctx
            let  logger = loggerFactory.CreateLogger<FreyaMiddleware>()

            if logger.IsEnabled LogLevel.Debug then
                let freq = double System.Diagnostics.Stopwatch.Frequency
                let stop = System.Diagnostics.Stopwatch.GetTimestamp()
                let elapsedMs = (double (stop - start)) * 1000.0 / freq

                logger.LogDebug(
                    "Freya returned {SomeNoneResult} for {HttpProtocol} {HttpMethod} at {Path} in {ElapsedMs}",
                    (if result.IsSome then "Some" else "None"),
                    ctx.Request.Protocol,
                    ctx.Request.Method,
                    ctx.Request.Path.ToString(),
                    elapsedMs)

            if (result.IsNone) then
#if TASKS
                return! next.Invoke ctx
        }
#else
#if HOPAC
                return! Job.awaitUnitTask (next.Invoke ctx)
        }
        |> Hopac.startAsTask :> Task
#else
                return! Async.AwaitTask (next.Invoke ctx)
        }
        |> Async.StartAsTask :> Task
#endif
#endif
