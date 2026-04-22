# QTO Plug-in – Specifica Tema UI (WPF/XAML)

**Data**: 22/04/2026
**Autore**: Luigi Dattilo
**Scope**: traduzione 1:1 dei design token del mockup [QTO-Mockup-Revit-UI.html](QTO-Mockup-Revit-UI.html) in risorse WPF/XAML pronte per essere implementate come `ResourceDictionary` in `App.xaml`.

> **Regola d'oro**: nessun colore, font o `Thickness` hardcoded nelle View. Tutti i token sono `{StaticResource …}` definiti in `Themes/QtoTheme.xaml`, merged in `App.Resources`. Questo permette in futuro un tema alternativo (light/dark) senza toccare le view.

---

## 1. Struttura file risorse

```
QtoRevitPlugin/
├── App.xaml                          ← merge dei ResourceDictionary
├── Themes/
│   ├── QtoTheme.xaml                 ← colori + font + spacing tokens
│   ├── QtoStyles.Buttons.xaml        ← Button / ToggleButton / RadioButton
│   ├── QtoStyles.Inputs.xaml         ← TextBox / ComboBox / CheckBox
│   ├── QtoStyles.DataGrid.xaml       ← DataGrid / DataGridColumnHeader
│   └── QtoStyles.Controls.xaml       ← TabControl / ProgressBar / Slider
├── Fonts/
│   ├── Archivo-Regular.ttf
│   ├── Archivo-SemiBold.ttf
│   ├── Archivo-Bold.ttf
│   ├── Archivo-ExtraBold.ttf
│   ├── IBMPlexSans-Regular.ttf
│   ├── IBMPlexSans-Medium.ttf
│   ├── IBMPlexSans-SemiBold.ttf
│   ├── JetBrainsMono-Regular.ttf
│   └── JetBrainsMono-SemiBold.ttf
└── Controls/
    ├── StatusChip.xaml(.cs)          ← UserControl riusabile
    ├── StatCard.xaml(.cs)
    └── SectionLabel.xaml(.cs)
```

Nel `QtoRevitPlugin.csproj`:
```xml
<ItemGroup>
  <Resource Include="Fonts\*.ttf" />
  <Page Include="Themes\*.xaml" Generator="MSBuild:Compile" SubType="Designer" />
</ItemGroup>
```

In `App.xaml`:
```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceDictionary Source="Themes/QtoTheme.xaml" />
      <ResourceDictionary Source="Themes/QtoStyles.Buttons.xaml" />
      <ResourceDictionary Source="Themes/QtoStyles.Inputs.xaml" />
      <ResourceDictionary Source="Themes/QtoStyles.DataGrid.xaml" />
      <ResourceDictionary Source="Themes/QtoStyles.Controls.xaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

---

## 2. Design Tokens — `Themes/QtoTheme.xaml`

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- ══════ FONTS ══════ -->
  <FontFamily x:Key="FontDisplay">pack://application:,,,/QtoRevitPlugin;component/Fonts/#Archivo</FontFamily>
  <FontFamily x:Key="FontBody">pack://application:,,,/QtoRevitPlugin;component/Fonts/#IBM Plex Sans</FontFamily>
  <FontFamily x:Key="FontMono">pack://application:,,,/QtoRevitPlugin;component/Fonts/#JetBrains Mono</FontFamily>

  <!-- Nota: il nome DOPO il # è la Font Family NAME (leggibile da Windows "Font Viewer",
       NON il nome del file). Se Archivo appare in Windows come "Archivo SemiBold" come
       nome variante, usare "Archivo" come family principale. -->

  <!-- ══════ COLORI — Revit chrome (dark) ══════ -->
  <Color x:Key="RevitBgColor">#1C1917</Color>
  <Color x:Key="RevitPanelColor">#252524</Color>
  <Color x:Key="RevitEdgeColor">#3A3936</Color>
  <Color x:Key="RevitRibbonColor">#33312D</Color>
  <Color x:Key="RevitTextColor">#E7E5E4</Color>
  <Color x:Key="RevitDimColor">#A8A29E</Color>

  <SolidColorBrush x:Key="RevitBgBrush"    Color="{StaticResource RevitBgColor}" />
  <SolidColorBrush x:Key="RevitPanelBrush" Color="{StaticResource RevitPanelColor}" />
  <SolidColorBrush x:Key="RevitEdgeBrush"  Color="{StaticResource RevitEdgeColor}" />
  <SolidColorBrush x:Key="RevitRibbonBrush" Color="{StaticResource RevitRibbonColor}" />
  <SolidColorBrush x:Key="RevitTextBrush"  Color="{StaticResource RevitTextColor}" />
  <SolidColorBrush x:Key="RevitDimBrush"   Color="{StaticResource RevitDimColor}" />

  <!-- ══════ COLORI — Plug-in content (light) ══════ -->
  <Color x:Key="PanelBgColor">#FAFAF9</Color>
  <Color x:Key="PanelSubColor">#F5F5F4</Color>
  <Color x:Key="PanelEdgeColor">#E7E5E4</Color>
  <Color x:Key="PanelEdgeStrongColor">#D6D3D1</Color>
  <Color x:Key="InkColor">#0C0A09</Color>
  <Color x:Key="InkDimColor">#57534E</Color>
  <Color x:Key="InkMutedColor">#78716C</Color>

  <SolidColorBrush x:Key="PanelBgBrush"         Color="{StaticResource PanelBgColor}" />
  <SolidColorBrush x:Key="PanelSubBrush"        Color="{StaticResource PanelSubColor}" />
  <SolidColorBrush x:Key="PanelEdgeBrush"       Color="{StaticResource PanelEdgeColor}" />
  <SolidColorBrush x:Key="PanelEdgeStrongBrush" Color="{StaticResource PanelEdgeStrongColor}" />
  <SolidColorBrush x:Key="InkBrush"             Color="{StaticResource InkColor}" />
  <SolidColorBrush x:Key="InkDimBrush"          Color="{StaticResource InkDimColor}" />
  <SolidColorBrush x:Key="InkMutedBrush"        Color="{StaticResource InkMutedColor}" />

  <!-- ══════ ACCENTI — engineering/cantiere ══════ -->
  <Color x:Key="AccentColor">#EA580C</Color>
  <Color x:Key="AccentDeepColor">#9A3412</Color>
  <Color x:Key="AccentSoftColor">#FED7AA</Color>
  <Color x:Key="AccentSurfaceColor">#FFF7ED</Color>
  <Color x:Key="AccentCoolColor">#075985</Color>

  <SolidColorBrush x:Key="AccentBrush"        Color="{StaticResource AccentColor}" />
  <SolidColorBrush x:Key="AccentDeepBrush"    Color="{StaticResource AccentDeepColor}" />
  <SolidColorBrush x:Key="AccentSoftBrush"    Color="{StaticResource AccentSoftColor}" />
  <SolidColorBrush x:Key="AccentSurfaceBrush" Color="{StaticResource AccentSurfaceColor}" />
  <SolidColorBrush x:Key="AccentCoolBrush"    Color="{StaticResource AccentCoolColor}" />

  <!-- ══════ STATI QTO — palette fissa (vedi §I5 spec) ══════ -->
  <Color x:Key="StComputatoColor">#16A34A</Color>
  <Color x:Key="StMancanteColor">#DC2626</Color>
  <Color x:Key="StAddedColor">#EA580C</Color>
  <Color x:Key="StParzialeColor">#CA8A04</Color>
  <Color x:Key="StMultiEpColor">#2563EB</Color>
  <Color x:Key="StEsclusoColor">#78716C</Color>

  <SolidColorBrush x:Key="StComputatoBrush" Color="{StaticResource StComputatoColor}" />
  <SolidColorBrush x:Key="StMancanteBrush"  Color="{StaticResource StMancanteColor}" />
  <SolidColorBrush x:Key="StAddedBrush"     Color="{StaticResource StAddedColor}" />
  <SolidColorBrush x:Key="StParzialeBrush"  Color="{StaticResource StParzialeColor}" />
  <SolidColorBrush x:Key="StMultiEpBrush"   Color="{StaticResource StMultiEpColor}" />
  <SolidColorBrush x:Key="StEsclusoBrush"   Color="{StaticResource StEsclusoColor}" />

  <!-- ══════ TIPOGRAFIA — scale modulari ══════ -->
  <!-- Display (Archivo) -->
  <system:Double x:Key="FontSizeViewTitle"    xmlns:system="clr-namespace:System;assembly=mscorlib">14</system:Double>
  <system:Double x:Key="FontSizeSectionLabel" xmlns:system="clr-namespace:System;assembly=mscorlib">10</system:Double>
  <system:Double x:Key="FontSizeRibbon"       xmlns:system="clr-namespace:System;assembly=mscorlib">10</system:Double>
  <system:Double x:Key="FontSizeChip"         xmlns:system="clr-namespace:System;assembly=mscorlib">10</system:Double>
  <system:Double x:Key="FontSizeButton"       xmlns:system="clr-namespace:System;assembly=mscorlib">11</system:Double>

  <!-- Body (IBM Plex Sans) -->
  <system:Double x:Key="FontSizeBody"         xmlns:system="clr-namespace:System;assembly=mscorlib">13</system:Double>
  <system:Double x:Key="FontSizeBodySmall"    xmlns:system="clr-namespace:System;assembly=mscorlib">12</system:Double>
  <system:Double x:Key="FontSizeLabel"        xmlns:system="clr-namespace:System;assembly=mscorlib">10.5</system:Double>

  <!-- Mono (JetBrains Mono) -->
  <system:Double x:Key="FontSizeMonoData"     xmlns:system="clr-namespace:System;assembly=mscorlib">11.5</system:Double>
  <system:Double x:Key="FontSizeMonoCaption"  xmlns:system="clr-namespace:System;assembly=mscorlib">10</system:Double>
  <system:Double x:Key="FontSizeStatValue"    xmlns:system="clr-namespace:System;assembly=mscorlib">22</system:Double>

  <!-- ══════ SPACING ══════ -->
  <Thickness x:Key="PaddingViewPanel">14,14,16,14</Thickness>
  <Thickness x:Key="PaddingCard">10,8,12,10</Thickness>
  <Thickness x:Key="PaddingButton">14,7,14,7</Thickness>
  <Thickness x:Key="PaddingButtonSmall">8,4,8,4</Thickness>
  <Thickness x:Key="PaddingInput">8,6,8,6</Thickness>
  <Thickness x:Key="PaddingChip">7,3,7,3</Thickness>

  <!-- ══════ CORNER RADIUS ══════ -->
  <!-- ATTENZIONE: il mockup è deliberatamente "squared" (radius 0/2px)
       per aderire all'estetica Revit/industriale. NON passare a radius 6+. -->
  <CornerRadius x:Key="RadiusNone">0</CornerRadius>
  <CornerRadius x:Key="RadiusSmall">2</CornerRadius>

</ResourceDictionary>
```

---

## 3. Font embedding — procedura

1. **Scarica i `.ttf`**:
   - Archivo: https://fonts.google.com/specimen/Archivo → pesi 400, 600, 700, 800
   - IBM Plex Sans: https://fonts.google.com/specimen/IBM+Plex+Sans → pesi 400, 500, 600
   - JetBrains Mono: https://fonts.google.com/specimen/JetBrains+Mono → pesi 400, 600

2. **Copia in `/Fonts/`** nel progetto VS.

3. **In `.csproj`** assicura che siano incluse come `Resource`:
   ```xml
   <ItemGroup>
     <Resource Include="Fonts\**\*.ttf" />
   </ItemGroup>
   ```

4. **Verifica il nome family** aprendo il `.ttf` con Font Viewer di Windows — nella riga "Font name" leggi es. `"Archivo"`. Usa quel nome dopo il `#`:
   ```xml
   <FontFamily x:Key="FontDisplay">pack://application:,,,/QtoRevitPlugin;component/Fonts/#Archivo</FontFamily>
   ```

5. **Problema comune**: se dopo il build il font non appare, controlla:
   - Il nome family dopo `#` (case-sensitive, con spazi)
   - L'Action del file nel Solution Explorer (deve essere **Resource**, non Content)
   - Il rebuild completo dopo aggiunta font (VS a volte non rileva cambi di file)

---

## 4. Stili chiave — `Themes/QtoStyles.Buttons.xaml`

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- ══════ Button · Base (equivalente .btn del mockup) ══════ -->
  <Style x:Key="QtoButtonBase" TargetType="Button">
    <Setter Property="FontFamily" Value="{StaticResource FontDisplay}" />
    <Setter Property="FontSize" Value="{StaticResource FontSizeButton}" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="Padding" Value="{StaticResource PaddingButton}" />
    <Setter Property="Background" Value="White" />
    <Setter Property="Foreground" Value="{StaticResource InkBrush}" />
    <Setter Property="BorderBrush" Value="{StaticResource PanelEdgeStrongBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="Bd"
                  Background="{TemplateBinding Background}"
                  BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}"
                  CornerRadius="0"
                  Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="{StaticResource PanelSubBrush}" />
              <Setter TargetName="Bd" Property="BorderBrush" Value="{StaticResource InkDimBrush}" />
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="{StaticResource PanelEdgeBrush}" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.5" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ══════ Button · Primary (arancio — azione principale) ══════ -->
  <Style x:Key="QtoButtonPrimary" TargetType="Button" BasedOn="{StaticResource QtoButtonBase}">
    <Setter Property="Background" Value="{StaticResource AccentBrush}" />
    <Setter Property="Foreground" Value="White" />
    <Setter Property="BorderBrush" Value="{StaticResource AccentDeepBrush}" />
    <Style.Triggers>
      <Trigger Property="IsMouseOver" Value="True">
        <Setter Property="Background" Value="{StaticResource AccentDeepBrush}" />
      </Trigger>
    </Style.Triggers>
  </Style>

  <!-- ══════ Button · Ghost (testo-only) ══════ -->
  <Style x:Key="QtoButtonGhost" TargetType="Button" BasedOn="{StaticResource QtoButtonBase}">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="Transparent" />
    <Setter Property="Foreground" Value="{StaticResource InkDimBrush}" />
    <Style.Triggers>
      <Trigger Property="IsMouseOver" Value="True">
        <Setter Property="Foreground" Value="{StaticResource AccentBrush}" />
        <Setter Property="Background" Value="{StaticResource AccentSurfaceBrush}" />
      </Trigger>
    </Style.Triggers>
  </Style>

  <!-- ══════ Button · Small (in grid toolbar, filter rules) ══════ -->
  <Style x:Key="QtoButtonSmall" TargetType="Button" BasedOn="{StaticResource QtoButtonBase}">
    <Setter Property="FontSize" Value="10" />
    <Setter Property="Padding" Value="{StaticResource PaddingButtonSmall}" />
  </Style>

</ResourceDictionary>
```

---

## 5. UserControl riusabili

### `Controls/SectionLabel.xaml`

Replica il titolo di sezione con il trattino arancione:

```xml
<UserControl x:Class="QtoPlugin.Controls.SectionLabel" ...>
  <StackPanel Orientation="Horizontal">
    <Rectangle Width="10" Height="1.5" Fill="{StaticResource AccentBrush}"
               VerticalAlignment="Center" Margin="0,0,6,0" />
    <TextBlock Text="{Binding Text, RelativeSource={RelativeSource AncestorType=UserControl}}"
               FontFamily="{StaticResource FontDisplay}"
               FontSize="{StaticResource FontSizeSectionLabel}"
               FontWeight="Bold"
               Foreground="{StaticResource InkMutedBrush}"
               TextBlock.LineHeight="1.2" />
  </StackPanel>
</UserControl>
```

Uso:
```xml
<qto:SectionLabel Text="Assegnazioni esistenti — Muro ID 112345" />
```

### `Controls/StatusChip.xaml`

Replica le chip colorate di stato (verde COMPUTATO, rosso MANCANTE ecc.):

```xml
<UserControl x:Class="QtoPlugin.Controls.StatusChip" ...>
  <Border Background="{Binding StatusBrush, RelativeSource={RelativeSource AncestorType=UserControl}}"
          Padding="5,1,5,1"
          CornerRadius="0">
    <TextBlock Text="{Binding Text, RelativeSource={RelativeSource AncestorType=UserControl}}"
               FontFamily="{StaticResource FontDisplay}"
               FontSize="9.5"
               FontWeight="SemiBold"
               Foreground="White" />
  </Border>
</UserControl>
```

DP `Stato` (enum) → setter del `StatusBrush` via converter che mappa:
- `Computato` → `StComputatoBrush`
- `Mancante` → `StMancanteBrush`
- `Added` → `StAddedBrush`
- ecc.

### `Controls/StatCard.xaml`

Il card numerico dell'Health Check (numero grande + label):

```xml
<UserControl x:Class="QtoPlugin.Controls.StatCard" ...>
  <Border Background="White" BorderBrush="{StaticResource PanelEdgeBrush}"
          BorderThickness="1" Padding="10,8,12,8">
    <Grid>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="3" />
        <ColumnDefinition Width="*" />
      </Grid.ColumnDefinitions>
      <Rectangle Grid.Column="0"
                 Fill="{Binding AccentBrush, RelativeSource={RelativeSource AncestorType=UserControl}}" />
      <StackPanel Grid.Column="1" Margin="8,0,0,0">
        <TextBlock Text="{Binding Value, ...}"
                   FontFamily="{StaticResource FontDisplay}"
                   FontSize="{StaticResource FontSizeStatValue}"
                   FontWeight="ExtraBold"
                   Foreground="{StaticResource InkBrush}" />
        <TextBlock Text="{Binding Label, ...}"
                   FontFamily="{StaticResource FontDisplay}"
                   FontSize="9.5"
                   FontWeight="SemiBold"
                   Foreground="{StaticResource InkMutedBrush}" />
      </StackPanel>
    </Grid>
  </Border>
</UserControl>
```

---

## 6. Stile DataGrid — `Themes/QtoStyles.DataGrid.xaml` (pattern chiave)

```xml
<Style TargetType="DataGrid" x:Key="QtoDataGrid">
  <Setter Property="FontFamily" Value="{StaticResource FontMono}" />
  <Setter Property="FontSize" Value="{StaticResource FontSizeMonoData}" />
  <Setter Property="Background" Value="White" />
  <Setter Property="BorderBrush" Value="{StaticResource PanelEdgeStrongBrush}" />
  <Setter Property="BorderThickness" Value="1" />
  <Setter Property="GridLinesVisibility" Value="Horizontal" />
  <Setter Property="HorizontalGridLinesBrush" Value="{StaticResource PanelEdgeBrush}" />
  <Setter Property="RowHeight" Value="24" />
  <Setter Property="HeadersVisibility" Value="Column" />
</Style>

<Style TargetType="DataGridColumnHeader" x:Key="QtoDataGridHeader">
  <Setter Property="Background" Value="{StaticResource InkBrush}" />
  <Setter Property="Foreground" Value="White" />
  <Setter Property="FontFamily" Value="{StaticResource FontDisplay}" />
  <Setter Property="FontSize" Value="9.5" />
  <Setter Property="FontWeight" Value="SemiBold" />
  <Setter Property="Padding" Value="8,5,8,5" />
  <Setter Property="HorizontalContentAlignment" Value="Left" />
  <!-- Uppercase via Typography -->
</Style>

<Style TargetType="DataGridRow" x:Key="QtoDataGridRow">
  <Style.Triggers>
    <Trigger Property="IsMouseOver" Value="True">
      <Setter Property="Background" Value="{StaticResource AccentSurfaceBrush}" />
    </Trigger>
    <Trigger Property="IsSelected" Value="True">
      <Setter Property="Background" Value="{StaticResource AccentSoftBrush}" />
      <Setter Property="Foreground" Value="{StaticResource InkBrush}" />
    </Trigger>
  </Style.Triggers>
</Style>
```

---

## 7. Do / Don't

✅ **Do**
- Dichiara tutti i brush in `QtoTheme.xaml` e referenzi con `{StaticResource …}` o `{DynamicResource …}`
- Rispetta la palette **6 stati fissi** (verde/rosso/arancione/giallo/blu/grigio) — è documentata in §I5 e nei filtri vista §I11
- Mantieni **Corner Radius = 0 o 2** (estetica industriale Revit)
- Usa `FontMono` per dati numerici in celle (quantità, prezzi, totali) per allineamento verticale delle cifre
- Usa `FontDisplay` (Archivo) per titoli, label sezioni, header colonne, pulsanti — è il font "di marca"
- Usa `FontBody` (IBM Plex Sans) per descrizioni, tooltip, testi lunghi

❌ **Don't**
- Mai hex hardcoded nelle View (`Background="#EA580C"` → SBAGLIATO, `Background="{StaticResource AccentBrush}"` → CORRETTO)
- Mai importare font via `System.Drawing.FontFamily` o WinForms
- Mai applicare `CornerRadius` > 4 (snatura l'aesthetic)
- Mai usare colori arcobaleno al di fuori della palette stati (es. viola, rosa non sono ammessi)
- Mai mischiare `TextBlock.FontFamily="Arial"` o `"Segoe UI"` — tutto via risorse

---

## 8. Riferimenti

- Mockup sorgente: [QTO-Mockup-Revit-UI.html](QTO-Mockup-Revit-UI.html)
- Palette stati documentata in: [QTO-Implementazioni-v3.md §I5](QTO-Implementazioni-v3.md)
- Filtri vista che usano la palette: [QTO-Implementazioni-v3.md §I11](QTO-Implementazioni-v3.md)
- Viste WPF enumerate: [QTO-Plugin-Documentazione-v3.md §2.1](QTO-Plugin-Documentazione-v3.md)
- Font embedding WPF (Microsoft docs): https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/packaging-fonts-with-applications
