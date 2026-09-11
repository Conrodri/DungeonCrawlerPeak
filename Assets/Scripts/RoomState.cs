// Tracks how much of a room the player has revealed on the minimap, plus whether it has been
// fully cleared of its original monsters - a permanent state, see RoomController.IsCleared.
public enum RoomState { Undiscovered, Adjacent, Discovered, Cleared }
