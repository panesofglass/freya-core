﻿namespace Freya.Core

open Aether

#if TASKS
open System.Threading.Tasks
open FSharp.Control.Tasks.V2
// open FSharp.Control.Tasks.V2.ContextInsensitive
#endif

#if HOPAC
open Hopac
#endif

// Core

// The common elements of all Freya based systems, namely the basic abstraction
// of an async state function over an OWIN environment, and tools for working
// with the environment in a functional and idiomatic way.

// Types

// Core types within the Freya codebase, representing the basic units of
// execution and composition, including the core async state carrying
// abstraction.

/// The core Freya type, representing a computation which is effectively a
/// State monad, with a concurrent return (the concurrency abstraction varies
/// based on the variant of Freya in use).

type Freya<'a> =
#if TASKS
    State -> Task<FreyaResult<'a>>
#else
#if HOPAC
    State -> Job<FreyaResult<'a>>
#else
    State -> Async<FreyaResult<'a>>
#endif
#endif

and FreyaResult<'a> =
#if STRUCT
    (struct ('a * State))
#else
    'a * State
#endif

/// The core Freya state type, containing the OWIN environment and other
/// metadata data structures which should be passed through a Freya
/// computation.

and State =
      /// The underlying request environment.
    { Environment: Environment
      /// Metadata associated with and bound to the lifetime of the request.
      Meta: Meta }

/// An alias for the commonly used OWIN data type of an
/// IDictionary<string,obj>.

and Environment =
    Microsoft.AspNetCore.Http.HttpContext

/// The Freya metadata data type containing data which should be passed through
/// a Freya computation but which is not relevant to non-Freya functions and so
/// is not considered part of the OWIN data model.

and Meta =
    /// Memoized value storage.
    { Memos: Map<System.Guid, obj> }


/// The Freya metadata data type containing data which should be passed through
/// a Freya computation but which is not relevant to non-Freya functions and so
/// is not considered part of the OWIN data model.

module Meta =
    /// A Lens to memoized values contained in request metadata.
    let memos_ : Lens<Meta,Map<System.Guid,obj>>=
        (fun x -> x.Memos),
        (fun m x -> { x with Memos = m })

    /// Provides an empty metadata object
    let empty =
        { Memos = Map.empty }

/// Patterns which allow destructuring Freya types.
[<AutoOpen>]
module Patterns =
    /// Destructures a `FreyaResult` into a value and the associated state.
    let inline (|FreyaResult|) (fr: FreyaResult<'a>) =
        match fr with
#if STRUCT
        | struct (a, s) -> (a, s)
#else
        | a, s -> (a, s)
#endif


/// The result of a Freya operation, combining a value with the associated
/// request state.
[<RequireQualifiedAccess>]
module FreyaResult =
    /// Destructures a `FreyaResult`, selecting the associated state.
    let inline (|State|) (fr: FreyaResult<'a>) =
        match fr with
#if STRUCT
        | struct (_, s) -> s
#else
        | _, s -> s
#endif

    /// Destructures a `FreyaResult`, selecting the value.
    let inline (|Value|) (fr: FreyaResult<'a>) =
        match fr with
#if STRUCT
        | struct (a, _) -> a
#else
        | a, _ -> a
#endif

    /// Constructs a `FreyaResult` from a value and some associated state.
    let inline create a s : FreyaResult<'a> =
#if STRUCT
        struct (a, s)
#else
        (a, s)
#endif

    /// Constructs a `FreyaResult` from a value and some associated state.
    ///
    /// Equivalent to `create` with the arguments flipped.
    let inline createWithState s a : FreyaResult<'a> =
        create a s

    /// A Lens from a `FreyaResult` to its value.
    let value_ : Aether.Lens<FreyaResult<'a>,'a> =
        (fun (Value a) -> a),
        (fun a (State s) -> create a s)

    /// A Lens from a `FreyaResult` to its associated state.
    let state_ : Aether.Lens<FreyaResult<'a>,State> =
        (fun (State s) -> s),
        (fun s (Value a) -> create a s)

// State

/// Basic optics for accessing elements of the State instance within the
/// current Freya function. The value_ lens is provided for keyed access
/// to the OWIN dictionary, and the memo_ lens for keyed access to the
/// memo storage in the Meta instance.

[<RequireQualifiedAccess>]
module State =
    open Aether.Operators

    /// A Lens from a `State` to its `Environment`.
    let environment_ : Lens<State,Environment> =
        (fun x -> x.Environment),
        (fun e x -> { x with Environment = e })

    /// A Lens from a `State` to associated metadata.
    let meta_ : Lens<State,Meta> =
        (fun x -> x.Meta),
        (fun m x -> { x with Meta = m })

    /// Creates a new `State` from a given `Environment`
    /// with no metadata.
    let create : Environment -> State =
        fun (env : Environment) ->
            { Environment = env
              Meta = Meta.empty }

    /// A lens from the Freya State to a memoized value of type 'a at a given
    /// Guid key.

    /// When working with this lens as an optic, the Some and None cases of
    /// optic carry semantic meaning, where Some indicates that the value is or
    /// should be present within the State, and None indicates that the value
    /// is not, or should not be present within the State.

    let memo_<'a> i =
            meta_
        >-> Meta.memos_
        >-> Map.value_ i
        >-> Option.mapIsomorphism box_<'a>

// Freya

/// Functions and type tools for working with Freya abstractions, particularly
/// data contained within the Freya state abstraction. Commonly defined
/// functions for treating the Freya functions as a monad, etc. are also
/// included, along with basic support for static inference.

[<RequireQualifiedAccess>]
module Freya =

    // Common

    // Commonly defined functions against the Freya types, particularly the
    // usual monadic functions (bind, apply, etc.). These are commonly used
    // directly within Freya programming but are also used within the Freya
    // computation expression defined later.

    /// The init (or pure) function, used to raise a value of type 'a to a
    /// value of type Freya<'a>.

    let init (a: 'a) : Freya<'a> =
        FreyaResult.create a
#if TASKS
        >> Task.FromResult
#else
#if HOPAC
        >> Job.result
#else
        >> async.Return
#endif
#endif

    /// The map function, used to map a value of type Freya<'a> to Freya<'b>,
    /// given a function 'a -> 'b.

    let map (a2b: 'a -> 'b) (aF: Freya<'a>) : Freya<'b> =
        fun s ->
#if TASKS
            task {
                let! (FreyaResult (a, s1)) = aF s
                return FreyaResult.create (a2b a) s1
            }
#else
#if HOPAC
            aF s |> Job.map (fun (FreyaResult (a, s1)) -> FreyaResult.create (a2b a) s1)
#else
            async.Bind (aF s, fun (FreyaResult (a, s1)) ->
                async.Return (FreyaResult.create (a2b a) s1))
#endif
#endif

    /// Takes two Freya values and maps them into a function
    let map2 (a2b2c: 'a -> 'b -> 'c) (aF: Freya<'a>) (bF: Freya<'b>) : Freya<'c> =
        fun s ->
#if TASKS
            task {
                let! (FreyaResult (a, s1)) = aF s
                let! (FreyaResult (b, s2)) = bF s1
                return FreyaResult.create (a2b2c a b) s2
            }
#else
#if HOPAC
            aF s |> Job.bind (fun (FreyaResult (a, s1)) ->
                bF s1 |> Job.map (fun (FreyaResult (b, s2)) ->
                    FreyaResult.create (a2b2c a b) s2))
#else
            async.Bind (aF s, fun (FreyaResult (a, s1)) ->
                async.Bind (bF s1, fun (FreyaResult (b, s2)) ->
                    async.Return (FreyaResult.create (a2b2c a b) s2)))
#endif
#endif

    /// Takes two Freya values and maps them into a function
    let map3 (a2b2c2d: 'a -> 'b -> 'c -> 'd) (aF: Freya<'a>) (bF: Freya<'b>) (cF: Freya<'c>) : Freya<'d> =
        fun s ->
#if TASKS
            task {
                let! (FreyaResult (a, s1)) = aF s
                let! (FreyaResult (b, s2)) = bF s1
                let! (FreyaResult (c, s3)) = cF s2
                return FreyaResult.create (a2b2c2d a b c) s3
            }
#else
#if HOPAC
            aF s |> Job.bind (fun (FreyaResult (a, s1)) ->
                bF s1 |> Job.bind (fun (FreyaResult (b, s2)) ->
                    cF s2 |> Job.map (fun (FreyaResult (c, s3)) ->
                        FreyaResult.create (a2b2c2d a b c) s3)))
#else
            async.Bind (aF s, fun (FreyaResult (a, s1)) ->
                async.Bind (bF s1, fun (FreyaResult (b, s2)) ->
                    async.Bind (cF s2, fun (FreyaResult (c, s3)) ->
                        async.Return (FreyaResult.create (a2b2c2d a b c) s3))))
#endif
#endif

    /// The Bind function for Freya, taking a Freya<'a> and a function
    /// 'a -> Freya<'b> and returning a Freya<'b>.

    let bind (a2bF: 'a -> Freya<'b>) (aF: Freya<'a>) : Freya<'b> =
        fun s ->
#if TASKS
            task {
                let! (FreyaResult (a, s1)) = aF s
                return! a2bF a s1
            }
#else
#if HOPAC
            aF s |> Job.bind (fun (FreyaResult (a, s1)) -> a2bF a s1)
#else
            async.Bind (aF s, fun (FreyaResult (a, s1)) -> a2bF a s1)
#endif
#endif

    /// The apply function for Freya function types, taking a function
    /// Freya<'a -> 'b> and a Freya<'a> and returning a Freya<'b>.

    let apply (aF: Freya<'a>) (a2Fb: Freya<'a -> 'b>) : Freya<'b> =
        fun s ->
#if TASKS
            task {
                let! (FreyaResult (a2b, s1)) = a2Fb s
                let! (FreyaResult (a, s2)) = aF s1
                return FreyaResult.create (a2b a) s2
            }
#else
#if HOPAC
            a2Fb s |> Job.bind (fun (FreyaResult (a2b, s1)) ->
                aF s1 |> Job.map (fun (FreyaResult (a, s2)) ->
                    FreyaResult.create (a2b a) s2))
#else
            async.Bind (a2Fb s, fun (FreyaResult (a2b, s1)) ->
                async.Bind (aF s1, fun (FreyaResult (a, s2)) ->
                    async.Return (FreyaResult.create (a2b a) s2)))
#endif
#endif

    /// The Left Combine function for Freya, taking two Freya<_> functions,
    /// composing their execution and returning the result of the first
    /// function.

    let combine (aF: Freya<'a>) (xF: Freya<'x>) : Freya<'a> =
        fun s ->
#if TASKS
            task {
                let! (FreyaResult.State s1) = xF s
                return! aF s1
            }
#else
#if HOPAC
            xF s |> Job.bind (fun (FreyaResult.State s1) -> aF s1)
#else
            async.Bind (xF s, fun (FreyaResult.State s1) -> aF s1)
#endif
#endif

    /// The Freya delay function, used to delay execution of a freya function
    /// by consuming a unit function to return the underlying Freya function.

    let delay (u2aF: unit -> Freya<'a>) : Freya<'a> =
#if HOPAC
        Job.delayWith (fun s -> u2aF () s)
#else
        fun s ->
            u2aF () s
#endif

    /// The identity function for Freya type functions.

    let identity (xF: Freya<_>) : Freya<_> =
        xF

    // Empty

    /// A simple convenience instance of an empty Freya function, returning
    /// the unit type. This can be required for various forms of branching logic
    /// etc. and is a convenience to save writing Freya.init () repeatedly.
    let empty : Freya<unit> =
        init ()

    /// The zero function, used to initialize a new function of Freya<unit>,
    /// effectively lifting the unit value to a Freya<unit> function.

    let zero () : Freya<unit> = empty

    // Extended

    // Some extended functions providing additional convenience outside of the
    // usual set of functions defined against Freya. In this case, interop with
    // the basic F# async system, and extended dual map function are given.

#if TASKS

    /// Converts a Task to a Freya
    let fromTask (aT: Task<'a>) : Freya<'a> =
        fun s -> task {
            let! a = aT
            return FreyaResult.createWithState s a
        }

    /// Lifts a function generating a Task to one creating a Freya
    let liftTask (a2bT: 'a -> Task<'b>) (a: 'a) : Freya<'b> =
        fun s -> task {
            let! b = a2bT a
            return FreyaResult.createWithState s b
        }

    /// Binds a Task to a function generating a Freya
    let bindTask (a2bF: 'a -> Freya<'b>) (aT: Task<'a>) : Freya<'b> =
        fun s -> task {
            let! a = aT
            return! a2bF a s
        }

#endif

#if HOPAC

    /// Converts a Hopac Job to a Freya
    let fromJob (aJ: Job<'a>) : Freya<'a> =
        fun s ->
            aJ |> Job.map (FreyaResult.createWithState s)

    /// Lifts a function generating a Hopac Job to one creating a Freya
    let liftJob (a2bJ: 'a -> Job<'b>) (a: 'a) : Freya<'b> =
        fun s ->
            a2bJ a |> Job.map (FreyaResult.createWithState s)

    /// Binds a Hopac Job to a function generating a Freya
    let bindJob (a2bF: 'a -> Freya<'b>) (aJ: Job<'a>) : Freya<'b> =
        fun s ->
            aJ |> Job.bind (fun a -> a2bF a s)

#endif

    /// Converts an Async to a Freya
    let fromAsync (aA: Async<'a>) : Freya<'a> =
        fun s ->
#if TASKS
            task {
                let! a = aA
                return FreyaResult.create a s
            }
#else
#if HOPAC
            Job.fromAsync aA |> Job.map (FreyaResult.createWithState s)
#else
            async.Bind (aA, fun a ->
                async.Return (FreyaResult.create a s))
#endif
#endif

    /// Lifts a function generating an Async to one creating a Freya
    let liftAsync (a2bA: 'a -> Async<'b>) (a: 'a) : Freya<'b> =
        fun s ->
#if TASKS
            task {
                let! b = a2bA a
                return FreyaResult.create b s
            }
#else
#if HOPAC
            a2bA a |> Job.fromAsync |> Job.map (FreyaResult.createWithState s)
#else
            async.Bind (a2bA a, fun b ->
                async.Return (FreyaResult.create b s))
#endif
#endif

    /// Binds an Async to a function generating a Freya
    let bindAsync (a2bF: 'a -> Freya<'b>) (aA: Async<'a>) : Freya<'b> =
        fun s ->
#if TASKS
            task {
                let! a = aA
                return! a2bF a s
            }
#else
#if HOPAC
            Job.fromAsync aA |> Job.bind (fun a -> a2bF a s)
#else
            async.Bind (aA, fun a -> a2bF a s)
#endif
#endif

    // Memoisation

    /// A function supporting memoisation of parameterless Freya functions
    /// (effectively a fully applied Freya expression) which will cache the
    /// result of the function in the Environment instance. This ensures that
    /// the function will be evaluated once per request/response on any given
    /// thread.
    let memo<'a> (aF: Freya<'a>) : Freya<'a> =
        let memo_ = State.memo_<'a> (System.Guid.NewGuid ())

        fun s ->
            match Aether.Optic.get memo_ s with
            | Some memo ->
#if TASKS
                Task.FromResult (FreyaResult.create memo s)
#else
#if HOPAC
                Job.result (FreyaResult.create memo s)
#else
                async.Return (FreyaResult.create memo s)
#endif
#endif
            | _ ->
#if TASKS
                task {
                    let! (FreyaResult (memo, s)) = aF s
                    return FreyaResult.create memo (Aether.Optic.set memo_ (Some memo) s)
                }
#else
#if HOPAC
                aF s |> Job.map (fun (FreyaResult (memo, s)) ->
                    (FreyaResult.create memo (Aether.Optic.set memo_ (Some memo) s)))
#else
                async.Bind (aF s, fun (FreyaResult (memo, s)) ->
                    async.Return (FreyaResult.create memo (Aether.Optic.set memo_ (Some memo) s)))
#endif
#endif

    /// Optic based access to the Freya computation state, analogous to the
    /// Optic.* functions exposed by Aether, but working within a Freya function
    /// and therefore part of the Freya ecosystem.
    [<RequireQualifiedAccess>]
    module Optic =

        /// A function to get a value within the current computation State
        /// given an optic from State to the required value.
        let inline get o : Freya<'a> =
#if TASKS
            fun s ->
                Task.FromResult (FreyaResult.create (Aether.Optic.get o s) s)
#else
#if HOPAC
            Job.lift (fun s -> FreyaResult.create (Aether.Optic.get o s) s)
#else
            fun s ->
                async.Return (FreyaResult.create (Aether.Optic.get o s) s)
#endif
#endif

        /// A function to set a value within the current computation State
        /// given an optic from State to the required value and an instance of
        /// the required value.
        let inline set o v : Freya<unit> =
#if TASKS
            fun (s: State) ->
                Task.FromResult (FreyaResult.create () (Aether.Optic.set o v s))
#else
#if HOPAC
            Job.lift (fun (s: State) -> FreyaResult.create () (Aether.Optic.set o v s))
#else
            fun (s: State) ->
                async.Return (FreyaResult.create () (Aether.Optic.set o v s))
#endif
#endif

        /// A function to map a value within the current computation State
        /// given an optic from the State the required value and a function
        /// from the current value to the new value (a homomorphism).
        let inline map o f : Freya<unit> =
#if TASKS
            fun (s: State) ->
                Task.FromResult (FreyaResult.create () (Aether.Optic.map o f s))
#else
#if HOPAC
            Job.lift (fun (s: State) -> FreyaResult.create () (Aether.Optic.map o f s))
#else
            fun (s: State) ->
                async.Return (FreyaResult.create () (Aether.Optic.map o f s))
#endif
#endif
