using FluentAssertions;
using PcTest.Ui.ViewModels;
using Xunit;

namespace PcTest.Ui.Tests.ViewModels;

/// <summary>
/// Tests for EditableViewModelBase dirty state tracking.
/// </summary>
public class EditableViewModelBaseTests
{
    private class TestEditableViewModel : EditableViewModelBase
    {
        private string _name = string.Empty;
        
        public string Name
        {
            get => _name;
            set
            {
                if (SetProperty(ref _name, value))
                {
                    CallMarkDirty();
                }
            }
        }
        
        public void CallMarkDirty() => MarkDirty();
        public void CallClearDirty() => ClearDirty();
        
        public override void Validate() { IsValid = !string.IsNullOrEmpty(Name); }
        public override Task SaveAsync() => Task.CompletedTask;
        public override void Discard() { _name = string.Empty; ClearDirty(); }
    }

    [Fact]
    public void IsDirty_ShouldBeFalse_WhenInitialized()
    {
        // Arrange & Act
        var vm = new TestEditableViewModel();

        // Assert
        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void IsDirty_ShouldBeTrue_WhenPropertyChanged()
    {
        // Arrange
        var vm = new TestEditableViewModel();

        // Act
        vm.Name = "New Name";

        // Assert
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsDirty_ShouldRemainTrue_AfterMultipleChanges()
    {
        // Arrange
        var vm = new TestEditableViewModel();

        // Act
        vm.Name = "Name 1";
        vm.Name = "Name 2";
        vm.Name = "Name 3";

        // Assert
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void ClearDirty_ShouldClearDirtyFlag()
    {
        // Arrange
        var vm = new TestEditableViewModel();
        vm.Name = "Changed";

        // Act
        vm.CallClearDirty();

        // Assert
        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void MarkDirty_ShouldSetDirtyFlag()
    {
        // Arrange
        var vm = new TestEditableViewModel();

        // Act
        vm.CallMarkDirty();

        // Assert
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void PropertyChanged_ShouldBeRaisedForIsDirty_WhenMarkedDirty()
    {
        // Arrange
        var vm = new TestEditableViewModel();
        var propertyChangedRaised = false;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(EditableViewModelBase.IsDirty))
                propertyChangedRaised = true;
        };

        // Act
        vm.Name = "Test";

        // Assert
        propertyChangedRaised.Should().BeTrue();
    }

    [Fact]
    public void SetProperty_ShouldNotMarkDirty_WhenValueUnchanged()
    {
        // Arrange
        var vm = new TestEditableViewModel();
        vm.Name = "Initial";
        vm.CallClearDirty();

        // Act
        vm.Name = "Initial"; // Same value

        // Assert
        vm.IsDirty.Should().BeFalse();
    }
}
