using UnityEngine;

public interface IWorldInteractable
{
    string InteractionPrompt { get; }
    float InteractionHoldDuration { get; }
    Transform InteractionTransform { get; }
    bool CanInteract(PlayerInteractor2D interactor);
    void Interact(PlayerInteractor2D interactor);
}
