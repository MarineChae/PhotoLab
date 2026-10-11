using PhotoLab.Core;
using PhotoLab.Imaging;

namespace PhotoLab.Desktop;

/// <summary>Bounds retained edits and combines one slider drag into one undo step.</summary>
public sealed class EditHistory
{
    public const int MAX_HISTORY_COUNT = 128;
    private readonly List<PhotoEditState> _states = [new PhotoEditState(new EditSettings())];
    private int _index;
    private int _groupIndex = -1;

    public EditSettings Current => CurrentState.Adjustments;
    public PhotoEditState CurrentState => _states[_index];
    public bool CanUndo => _index > 0;
    public bool CanRedo => _index + 1 < _states.Count;
    public int Count => _states.Count;

    /// <summary>Detached immutable timeline; source/restored edits cannot mutate a captured photo history.</summary>
    public sealed class Snapshot
    {
        internal IReadOnlyList<PhotoEditState> States { get; }
        internal int Index { get; }
        public PhotoEditState CurrentState => States[Index];
        internal Snapshot(PhotoEditState[] states, int index)
        {
            States = Array.AsReadOnly(states);Index = index;
        }
    }

    public Snapshot Capture()
    {
        EndGroup();
        return new Snapshot(_states.ToArray(),_index);
    }

    public void Restore(Snapshot snapshot)
    {
        _states.Clear();_states.AddRange(snapshot.States);
        _index = snapshot.Index;_groupIndex = -1;
    }

    public void Reset(EditSettings settings)
        => ResetState(new PhotoEditState(settings));

    public void ResetState(PhotoEditState state)
    {
        _states.Clear(); _states.Add(state);
        _index = 0; _groupIndex = -1;
    }

    public void BeginGroup() => _groupIndex = _index;
    public void EndGroup() => _groupIndex = -1;

    public void Apply(EditSettings settings)
        => ApplyState(CurrentState with { Adjustments = settings });

    public void ApplyState(PhotoEditState state)
    {
        if (state == CurrentState) return;
        if (CanRedo) _states.RemoveRange(_index + 1, _states.Count - _index - 1);
        if (_groupIndex >= 0 && _index > _groupIndex) _states[_index] = state;
        else { _states.Add(state); _index++; }
        if (_states.Count > MAX_HISTORY_COUNT)
        {
            _states.RemoveAt(0); _index--;
            if (_groupIndex >= 0) _groupIndex = Math.Max(0, _groupIndex - 1);
        }
    }

    public bool Move(int step)
    {
        EndGroup();
        int next = _index + step;
        if (next < 0 || next >= _states.Count) return false;
        _index = next;
        return true;
    }

    /// <summary>Inspect an undo/redo target before asynchronous RAW development; failed development must not move history.</summary>
    public PhotoEditState? Peek(int step)
    {
        long next = (long)_index + step;
        return next < 0 || next >= _states.Count ? null : _states[(int)next];
    }
}
