namespace VisualInspection.Core.Configuration;

public enum ModelFormat
{
    Onnx,
    Pt
}

public enum ModelTaskType
{
    Detection,
    Classification,
    Segmentation,
    Pose,
    Temporal
}

public enum LabelSourceMode
{
    Manual,
    ImportedFromModel
}

public enum TestItemType
{
    Normal,
    PoseSequence
}

public enum RegionType
{
    FullImage,
    Roi
}

public enum InputSourceType
{
    Folder,
    DirectShowCamera,
    VendorCamera
}

public enum RuntimeSourcePolicy
{
    Fixed,
    OperatorSelectable
}

public enum InvalidFileBehavior
{
    Skip,
    Stop
}

public enum FolderSortOrder
{
    NaturalFileName,
    LastWriteTime
}
