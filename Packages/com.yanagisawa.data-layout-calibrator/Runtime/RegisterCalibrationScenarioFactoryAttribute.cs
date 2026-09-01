using System;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>
    /// Registers a scenario factory with the compile-time generated registry in the
    /// assembly that owns this attribute. Registration is explicit so player builds
    /// never depend on reflection, type scanning, or managed-code preservation rules.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class RegisterCalibrationScenarioFactoryAttribute : Attribute
    {
        public RegisterCalibrationScenarioFactoryAttribute(Type factoryType)
        {
            FactoryType = factoryType;
        }

        public Type FactoryType { get; }
    }
}
