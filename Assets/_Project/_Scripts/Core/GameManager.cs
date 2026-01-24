using UnityEngine;

public class GameManager : Singleton<GameManager>
{
    public enum GameState
    {
        Bootstrap,
        Lobby,
        Game
    }

    public GameState CurrentState { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        Debug.Log("GameManager Initialized");
    }

    public void SetGameState(GameState newState)
    {
        CurrentState = newState;
        Debug.Log($"[GameManager] State changed to: {newState}");
    }
}
