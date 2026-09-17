using System;
using UnityEngine;

/// <summary>
/// Centraliza o encerramento bem-sucedido de uma tarefa.
/// </summary>
public sealed class DuplicantTaskFinisher
{
    private readonly DuplicantController controller;
    private readonly DuplicantMovement movement;

    public DuplicantTaskFinisher(
        DuplicantController controller,
        DuplicantMovement movement)
    {
        this.controller = controller;
        this.movement = movement;
    }

    public void Finish(
        Action releaseAllReservations,
        Action clearGroundHaulTracking)
    {
        releaseAllReservations?.Invoke();
        clearGroundHaulTracking?.Invoke();

        controller.currentTask = null;
        controller.currentState =
            DuplicantController.WorkerState.Idle;

        if (movement.ShouldFall())
        {
            controller.StartCoroutine(
                movement.HandleFallingRoutine());
        }
    }
}