using System.Collections;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Abstraction that lets plain C# classes start and stop coroutines through a MonoBehaviour host.
    /// </summary>
    /// <remarks>
    /// <para><b>Ownership:</b> Callers must keep the returned <see cref="Coroutine"/> handle to stop it later.</para>
    /// <para><b>StopCoroutine(null):</b> Implementations may throw; callers should avoid passing null.</para>
    /// <para><b>Host lifecycle:</b> If the host is destroyed (e.g. scene unload), running coroutines stop automatically.</para>
    /// <para><b>Invalid handles:</b> Stopping an already-stopped or invalid handle is implementation-defined; prefer guarding with null checks.</para>
    /// </remarks>
    public interface ICoroutineRunner
    {
        /// <summary>Starts the given coroutine and returns a handle for stopping it.</summary>
        Coroutine StartCoroutine(IEnumerator routine);

        /// <summary>Stops the coroutine. Do not pass null.</summary>
        void StopCoroutine(Coroutine routine);
    }

    /// <summary>Adapts delegate-based Start/Stop to ICoroutineRunner.</summary>
    public sealed class DelegatingCoroutineRunner : ICoroutineRunner
    {
        private readonly System.Func<IEnumerator, Coroutine> _start;
        private readonly System.Action<Coroutine> _stop;

        public DelegatingCoroutineRunner(System.Func<IEnumerator, Coroutine> start, System.Action<Coroutine> stop)
        {
            _start = start;
            _stop = stop;
        }

        public Coroutine StartCoroutine(IEnumerator routine) => _start(routine);
        public void StopCoroutine(Coroutine routine) => _stop(routine);
    }
}
