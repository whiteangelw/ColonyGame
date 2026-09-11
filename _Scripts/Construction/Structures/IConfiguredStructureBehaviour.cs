public interface IConfiguredStructureBehaviour
{
    bool UsesSpecializedSaveData { get; }
    void InitializeStructureBehaviour(ConfiguredStructure structure);
    void ShutdownStructureBehaviour();
}
