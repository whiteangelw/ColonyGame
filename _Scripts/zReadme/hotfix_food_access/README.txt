HOTFIX — ACESSO E RESERVA DE COMIDA

Substitua os cinco scripts pelos arquivos desta pasta.

Correções:
- O trabalho atual é cancelado antes de registrar a rotina de alimentação.
- Cancelamentos externos interrompem também a rotina alimentar e liberam sua reserva uma única vez.
- O plano preserva a posição exata de interação escolhida junto ao baú.
- Recálculos de rota continuam mirando essa posição, sem trocar silenciosamente de lado.
- O DevMode mostra a posição de interação do plano.
- As falhas de rota, fonte destruída e reserva perdida agora possuem detalhes distintos.

Teste recomendado:
1. Coloque comida em um baú acessível e zere a fome.
2. Faça o duplicant iniciar outra tarefa antes de buscar comida.
3. Durante o trajeto, altere um bloco sem bloquear todas as rotas.
4. Repita bloqueando completamente o baú.
5. Repita com vários duplicants disputando a comida.

Resultado esperado:
- Com rota alternativa, ele recalcula e consome a reserva.
- Sem rota, ele libera a reserva, fica Idle e tenta novamente após o cooldown.
- Nenhuma coroutine continua tentando consumir uma reserva já cancelada.
