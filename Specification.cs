namespace COMBinator
{
    /// <summary>
    /// Enummeration for kind of provided information.
    /// </summary>
    public enum Specification
    {
        None,           // no information provided -> evaluation impossible
        ModeAndSigns,   // mode number and both signs -> evaluation needless
        ModeOnly,       // mode number only -> evaluation impossible (4 solutions)
        TargetAndSigns, // estimated laser frequency and both signs -> mode number
        TargetAndMode,  // estimated laser frequency and mode number -> both signs
        TargetOnly,     // estimated laser frequency -> mode number and both signs
        Target633       // 633 nm CCL frequency -> HFS, mode number and both signs
    }
}
