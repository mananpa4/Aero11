# Aero11 Installer

Instalador gráfico x64 para Windows 11 builds 22000 a 28000.1896.

## Seguridad y recuperación

- Solicita elevación mediante manifiesto antes de escribir en `Windows\Resources\Themes`.
- Valida que todas las rutas extraídas permanezcan dentro del directorio temporal.
- Guarda cualquier instalación anterior en `%ProgramData%\Aero11\Backups`.
- El botón **Restaurar copia** mueve la instalación actual a una carpeta recuperable y repone la copia más reciente.
- SecureUxTheme se instala mediante su MSI; la restauración del tema no lo desinstala.
- No modifica archivos protegidos de Windows directamente.

## Compilar

Primero debe existir `repos\SecureUxTheme-master\bin\Release_x64\SecureUxTheme.msi`.

```powershell
dotnet publish .\installer\Aero11.Installer\Aero11.Installer.csproj -c Release -r win-x64 -o .\dist
```

El resultado es `dist\Aero11-Installer.exe` y no necesita un runtime de .NET instalado.

También se genera `dist\SecureUxTheme-ForceUninstaller.exe`. Este detecta
instalaciones MSI, hooks IFEO y archivos v3/v4 huérfanos, guarda una copia en
`%ProgramData%\Aero11\SecureUxThemeCleanup` y luego retira únicamente las
entradas asociadas a SecureUxTheme.

Después de instalar SecureUxTheme, Aero11 Installer ofrece cerrar sesión. Si
se acepta, copia el instalador a `%ProgramData%\Aero11\Resume`, guarda el tema
seleccionado y crea la tarea `Aero11 ResumeAfterLogoff` para el usuario actual.
Al volver a iniciar sesión la tarea abre el instalador con privilegios altos;
este elimina inmediatamente la tarea y pregunta si se desea continuar antes
de abrir el tema. El estado se elimina tanto al aceptar como al cancelar.

`scripts\Resume-Aero11AfterLogoff.ps1` permite preparar manualmente el mismo
flujo si fuera necesario.
