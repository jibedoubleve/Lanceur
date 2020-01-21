﻿using Probel.Lanceur.Core.Entities;

namespace Probel.Lanceur.Core.Services
{
    /// <summary>
    /// Manage all the macro of the application. A Macro is a special 
    /// reserved word. It is used for instance launch multiple alias
    /// in one keyword
    /// </summary>
    public interface IMacroService
    {
        #region Methods

        void Handle(Alias cmd);

        bool Has(string name);

        IMacroService With(ICommandRunner cmdrunner, IAliasService aliasService);

        #endregion Methods
    }
}