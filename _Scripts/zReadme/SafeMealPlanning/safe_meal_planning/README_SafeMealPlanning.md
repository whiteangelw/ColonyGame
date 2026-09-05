# Safe Meal Planning — configuração e testes

## Arquivos

Substitua os scripts existentes pelos arquivos de mesmo nome e adicione
`FoodPlanningModels.cs` ao projeto. Não mantenha cópias antigas das mesmas
classes dentro de `Assets`, pois o Unity compilaria definições duplicadas.

## LifeCycleSettingsSO

Valores iniciais recomendados:

- `Preventive Meal Target`: 0.75
- `Critical Meal Target`: 0.95
- `Emergency Meal Target`: 1.00
- `Raw Food Allowed Below`: 0.10
- `Safe Arrival Hunger Margin`: 0.10
- `Preventive Food Search Radius`: 120
- `Critical Food Search Radius`: 80
- `Emergency Food Search Radius`: 40
- `Maximum Food Path Checks`: 8
- `Maximum Food Searches Per Frame`: 2
- `Travel Time Safety Multiplier`: 1.35
- `Food Eating Duration Per Portion`: 1.2
- `Food Search Retry Delay`: 3

Os raios são medidos em células do Grid. A comida crua passa a ser permitida
quando a fome está em 10% ou menos. Use `0` caso queira permitir flora e itens
emergenciais somente quando a fome estiver completamente zerada.

## ItemDataSO de alimentos

Configure em cada alimento:

- `Is Food`: marcado
- `Hunger Restored`: recuperação por unidade
- `Is Raw Food`: marque para cogumelos/frutas cruas
- `Food Quality`: 0 a 5
- `Allow Preventive Consumption`: permite comer o item antes da fase crítica

O asset precisa continuar cadastrado no `ItemDatabaseSO` usado pelo
`ItemSpawner`.

## FloraDefinitionSO

Configure:

- `Food Resource Type`: tipo usado no diagnóstico
- `Initial Portions`: quantidade disponível na planta
- `Hunger Restored Per Portion`: recuperação por porção
- `Is Raw Food`: normalmente marcado
- `Food Quality`: normalmente 0 para flora selvagem

## DuplicantStatusEffects

Não configure os campos desse componente no prefab. Eles mostram somente o
estado atual do efeito. Chance, duração e penalidade continuam configuradas no
`LifeCycleSettingsSO`. O `DuplicantController` garante o componente em runtime.

## Testes essenciais

1. Zere a fome com dez refeições de valor 10 no baú. O duplicant deve fazer
   uma viagem e consumir porções até alcançar 100.
2. Coloque um baú muito distante e um alimento cru próximo. Com fome crítica,
   o alimento próximo deve vencer quando o baú exceder o raio seguro.
3. Bloqueie o caminho durante a viagem. A reserva deve ser liberada e o
   duplicant deve procurar novamente após o cooldown.
4. Use dois duplicants e comida limitada. A mesma unidade não pode ser
   consumida ou transportada duas vezes.
5. Deixe parte de uma pilha de comida no chão. Enquanto estiver reservada para
   refeição, ela não deve ser coletada para o baú; a sobra volta ao hauling.
6. Salve durante a caminhada. Ao carregar, fome e efeitos permanecem, mas a
   reserva transitória é recalculada.
7. Teste 50 duplicants. O DevMode pode mostrar `SearchDeferred`; isso significa
   que a busca foi distribuída entre frames, não que houve uma falha.
