public enum MovementType
{
    Walk,       // Deslocamento padrão sobre chão plano
    StepUp,     // Subir degrau / escalar pequenos blocos
    StepDown,   // Descer degrau / queda controlada
    JumpGap,    // Salto horizontal sobre o vazio
    ClimbLadder // Subida ou descida em escadas
}