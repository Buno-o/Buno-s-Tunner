// =====================================================================
//  BunoTunner  -  controle de ventoinhas / OC / sensores (visual Windows XP)
//  Compila com o csc.exe que ja vem no Windows (C# 5, .NET Framework 4.x)
//  Veja LEIA-ME.txt
// =====================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Management;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net;
using Microsoft.Win32;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using LHM = LibreHardwareMonitor.Hardware;
using Th = System.Threading;

namespace BunoTunner
{
    // ------------------------------------------------------------------
    //  Entrada. As DLLs do LibreHardwareMonitor ficam embutidas no .exe
    // ------------------------------------------------------------------
    // Registro de tempos da abertura (BunoTunner_startup.log) + tela de abertura
    static class Startup
    {
        static Stopwatch sw = Stopwatch.StartNew();
        static long last;
        static StringBuilder log = new StringBuilder();
        static object lk = new object();
        public static string Status = "Iniciando...";
        public static void Mark(string what)
        {
            lock (lk)
            {
                long now = sw.ElapsedMilliseconds;
                log.AppendLine(now.ToString().PadLeft(6) + " ms  (+" + (now - last).ToString().PadLeft(5) + ")  " + what);
                last = now;
                Status = what;
            }
        }
        public static double Seconds { get { return sw.ElapsedMilliseconds / 1000.0; } }
        public static void Save()
        {
            try
            {
                lock (lk)
                    System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BunoTunner_startup.log"),
                        "BunoTunner - tempos da abertura (cada linha: tempo total, tempo desta etapa)\r\n" + log.ToString());
            }
            catch (Exception) { }
        }

        static Th.Thread st; static SplashForm sp;
        public static void ShowSplash()
        {
            try
            {
                Th.ManualResetEvent ready = new Th.ManualResetEvent(false);
                st = new Th.Thread(delegate ()
                {
                    sp = new SplashForm();
                    sp.Shown += delegate { ready.Set(); };
                    try { Application.Run(sp); } catch (Exception) { ready.Set(); }
                });
                st.SetApartmentState(Th.ApartmentState.STA);
                st.IsBackground = true; st.Start();
                ready.WaitOne(3000);
            }
            catch (Exception) { }
        }
        public static void CloseSplash()
        {
            try { SplashForm f = sp; if (f != null && f.IsHandleCreated) f.BeginInvoke(new Action(f.Close)); }
            catch (Exception) { }
        }
    }

    class SplashForm : Form
    {
        Timer t = new Timer(); int tick;
        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false; TopMost = true; Size = new Size(380, 120);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            t.Interval = 90; t.Tick += delegate { tick++; Invalidate(); }; t.Start();
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { t.Stop(); t.Dispose(); base.OnFormClosed(e); Application.ExitThread(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Xp.Face);
            using (Pen p = new Pen(Xp.CapFrame, 2)) g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
            using (Font f = new Font(Xp.Ui.FontFamily, 15f, FontStyle.Bold))
                TextRenderer.DrawText(g, "BunoTunner", f, new Point(18, 14), Xp.Text);
            TextRenderer.DrawText(g, Lang.T("Carregando sensores e hardware..."), Xp.Ui, new Point(20, 50), Xp.Text);
            string st = Lang.T(Startup.Status ?? "");
            if (st.Length > 52) st = st.Substring(0, 52);
            TextRenderer.DrawText(g, st, Xp.Ui, new Point(20, 68), Xp.Dim);
            int x0 = 20, w = Width - 40, bw = 70, pos = (tick * 9) % (w + bw) - bw;
            using (SolidBrush bg = new SolidBrush(Xp.ProgBg)) g.FillRectangle(bg, x0, 94, w, 10);
            g.SetClip(new Rectangle(x0, 94, w, 10));
            using (SolidBrush fg = new SolidBrush(Xp.Prog2)) g.FillRectangle(fg, x0 + pos, 94, bw, 10);
        }
    }

    // ------------------------------------------------------------------
    //  Idiomas: padrao = idioma do Windows (portugues ou ingles); o texto original do programa e em portugues.
    //  A traducao e feita por frases/trechos (do maior para o menor), so na hora de mostrar:
    //  rotulos, botoes, titulos de janela, dicas, menus, listas, caixas de mensagem e textos que mudam durante o uso.
    // ------------------------------------------------------------------
    class LTip : ToolTip
    {
        readonly Dictionary<Control, string> src = new Dictionary<Control, string>();
        public LTip() { Lang.Tips.Add(this); }
        public new void SetToolTip(Control c, string s)
        {
            if (c == null) return;
            if (!src.ContainsKey(c)) { Control cc = c; c.Disposed += delegate { src.Remove(cc); }; }
            src[c] = s;
            base.SetToolTip(c, Lang.T(s));
        }
        public void Retranslate()
        {
            foreach (KeyValuePair<Control, string> kv in new List<KeyValuePair<Control, string>>(src)) base.SetToolTip(kv.Key, Lang.T(kv.Value));
        }
    }

    // MessageBox com o texto/titulo traduzidos
    static class Dlg
    {
        public static DialogResult Show(string text) { return MessageBox.Show(Lang.T(text)); }
        public static DialogResult Show(string text, string caption) { return MessageBox.Show(Lang.T(text), Lang.T(caption)); }
        public static DialogResult Show(string text, string caption, MessageBoxButtons b) { return MessageBox.Show(Lang.T(text), Lang.T(caption), b); }
        public static DialogResult Show(string text, string caption, MessageBoxButtons b, MessageBoxIcon i) { return MessageBox.Show(Lang.T(text), Lang.T(caption), b, i); }
        public static DialogResult Show(IWin32Window o, string text) { return MessageBox.Show(o, Lang.T(text)); }
        public static DialogResult Show(IWin32Window o, string text, string caption) { return MessageBox.Show(o, Lang.T(text), Lang.T(caption)); }
        public static DialogResult Show(IWin32Window o, string text, string caption, MessageBoxButtons b) { return MessageBox.Show(o, Lang.T(text), Lang.T(caption), b); }
        public static DialogResult Show(IWin32Window o, string text, string caption, MessageBoxButtons b, MessageBoxIcon i) { return MessageBox.Show(o, Lang.T(text), Lang.T(caption), b, i); }
    }

    static class Lang
    {
        public static int Cur;   // 0 = portugues, 1 = ingles
        public static List<LTip> Tips = new List<LTip>();
        public static event Action Changed;
        static Dictionary<string, string> en = new Dictionary<string, string>();
        static Dictionary<char, string[]> byFirst = new Dictionary<char, string[]>();
        static Dictionary<string, string> cache = new Dictionary<string, string>();
        static List<WeakReference> menus = new List<WeakReference>();
        sealed class Holder { public string Src; public bool Hooked; }
        static ConditionalWeakTable<object, Holder> hold = new ConditionalWeakTable<object, Holder>();
        static bool busy;
        static Timer sweeper;
        static readonly string[] Pairs = {
            "Placa de video:", "Graphics card:",
            "Tema:", "Theme:",
            "Ventoinhas\nAutomatico, manual (% e RPM) e curva de temperatura", "Fans\nAutomatic, manual (% and RPM) and temperature curve",
            "Overclock\nCore clock, memory clock, TDP, limite de temperatura e travar clocks", "Overclock\nCore clock, memory clock, TDP, temperature limit and clock locking",
            "Especificacoes da GPU\nTodos os dados da placa de video", "GPU specifications\nAll graphics card data",
            "Especificacoes da CPU\nProcessador, nucleos, cache, clocks e conjuntos de instrucoes", "CPU specifications\nProcessor, cores, cache, clocks and instruction sets",
            "Benchmark / Estresse\nGPU, CPU, FPU, RAM e SSD com verificacao de erros e limite de temperatura", "Benchmark / Stress test\nGPU, CPU, FPU, RAM and SSD with error checking and temperature limit",
            "Memoria RAM\nVelocidade, canais, voltagem, modulos e timings basicos (tCAS, tRCD, tRP, tRAS, tRC, tRFC, Command Rate)", "RAM\nSpeed, channels, voltage, modules and basic timings (tCAS, tRCD, tRP, tRAS, tRC, tRFC, Command Rate)",
            "Saude do armazenamento\nVida util restante, temperatura e S.M.A.R.T. de SSDs NVMe/SATA e HDs", "Storage health\nRemaining life, temperature and S.M.A.R.T. of NVMe/SATA SSDs and HDDs",
            "Internet\nTeste de velocidade (ping, download, upload) e informacoes da rede", "Internet\nSpeed test (ping, download, upload) and network information",
            "Afinidade de processos\nEscolha em quais nucleos cada programa roda; presets e regras automaticas", "Process affinity\nChoose which cores each program runs on; presets and automatic rules",
            "Desinstalador\nRemove programas por completo: pastas, registro, inicializacao, servicos e tarefas que sobram; tambem remove o que se recusa a desinstalar", "Uninstaller\nRemoves programs completely: leftover folders, registry, startup entries, services and tasks; also removes what refuses to uninstall",
            "Otimizacoes do Windows\nLimpeza de temporarios, compressao de memoria e outros ajustes", "Windows optimizations\nTemp cleanup, memory compression and other tweaks",
            "Sensores\nTudo em uma janela: GPU, CPU, placa-mae, memoria, discos e ventoinhas, com grafico, busca e alertas", "Sensors\nEverything in one window: GPU, CPU, motherboard, memory, disks and fans, with graph, search and alerts",
            "Limpar arquivos temporarios", "Clean temporary files",
            "Apaga C:\\Windows\\Temp, Prefetch e a pasta %temp% do usuario (itens em uso sao ignorados). Nenhum documento pessoal e tocado.", "Deletes C:\\Windows\\Temp, Prefetch and the user %temp% folder (files in use are skipped). No personal documents are touched.",
            "Desativar compressao de memoria", "Disable memory compression",
            "Executa Disable-MMAgent -mc: o Windows deixa de comprimir a RAM (usa um pouco menos de CPU, um pouco mais de RAM). Vale apos reiniciar o PC.", "Runs Disable-MMAgent -mc: Windows stops compressing RAM (slightly less CPU, slightly more RAM used). Takes effect after a reboot.",
            "Menu de clique direito do Windows 10", "Windows 10 right-click menu",
            "No Windows 11, traz de volta o menu de clique direito completo e classico do Windows 10 (sem o \"Mostrar mais opcoes\"). Vale ao reiniciar o Explorer ou fazer logoff.", "On Windows 11, brings back the full classic Windows 10 right-click menu (no \"Show more options\"). Takes effect after restarting Explorer or signing out.",
            "Desativar MPO (Multiplane Overlay)", "Disable MPO (Multiplane Overlay)",
            "Define OverlayTestMode=5 no DWM. Ajuda a resolver tremidas, telas piscando e telas pretas em alguns monitores/placas NVIDIA e AMD. Reinicie o PC para valer.", "Sets OverlayTestMode=5 in DWM. Helps with stutter, flickering and black screens on some monitors/NVIDIA and AMD cards. Reboot to apply.",
            "Overlay DLSS (indicador na tela)", "DLSS overlay (on-screen indicator)",
            "Mostra no canto do jogo um indicador quando o DLSS esta em uso (so placas NVIDIA RTX). Aplica na proxima vez que abrir o jogo.", "Shows an indicator in the game corner when DLSS is in use (NVIDIA RTX only). Applies next time the game starts.",
            "Isolamento de nucleo (Integridade de memoria)", "Core isolation (Memory integrity)",
            "Recurso de seguranca do Windows (HVCI) que isola o nucleo do sistema usando virtualizacao. Desativar pode dar mais FPS em jogos e liberar drivers antigos, mas reduz a protecao. Reinicie o PC.", "Windows security feature (HVCI) that isolates the system core using virtualization. Disabling may give more FPS in games and allow old drivers, but lowers protection. Reboot required.",
            "Hibernacao", "Hibernation",
            "Liga ou desliga a hibernacao (powercfg /hibernate). Desligada, apaga o hiberfil.dat (libera varios GB no disco) e tambem desativa a Inicializacao Rapida.", "Turns hibernation on or off (powercfg /hibernate). When off, deletes hiberfil.sys (frees several GB) and also disables Fast Startup.",
            "Aceleracao do mouse (Aprimorar precisao do ponteiro)", "Mouse acceleration (Enhance pointer precision)",
            "Desligada, o ponteiro anda sempre a mesma distancia para o mesmo movimento da mao (recomendado para jogos). Aplica na hora, sem reiniciar.", "When off, the pointer always moves the same distance for the same hand movement (recommended for games). Applies instantly, no reboot.",
            "Mitigacoes do Windows (Spectre / Meltdown)", "Windows mitigations (Spectre / Meltdown)",
            "Desativa as mitigacoes de execucao especulativa (FeatureSettingsOverride=3) para ganhar desempenho. REDUZ A SEGURANCA do PC; use so em maquina de jogos sem dados sensiveis. Reinicie o PC.", "Disables speculative-execution mitigations (FeatureSettingsOverride=3) for performance. LOWERS PC SECURITY; use only on a gaming machine without sensitive data. Reboot required.",
            "Limpar", "Clean",
            "Desativar", "Disable",
            "Ativar", "Enable",
            "Reativar", "Re-enable",
            "Desfazer", "Undo",
            "Padrao do Windows", "Windows default",
            "Desativar MPO", "Disable MPO",
            "Executando...", "Running...",
            "GPU  -  render 3D (estilo FurMark)", "GPU  -  3D render (FurMark style)",
            "Abre uma janela OpenGL com shader e pelos e mostra o FPS. Fechar a janela para o teste.", "Opens an OpenGL window with shaders and fur and shows FPS. Closing the window stops the test.",
            "CPU  -  ray tracing 3D (estilo Cinebench)", "CPU  -  3D ray tracing (Cinebench style)",
            "Renderiza uma cena em todos os nucleos, bloco a bloco. Pontos = Mrays/s x 100.", "Renders a scene on all cores, block by block. Points = Mrays/s x 100.",
            "FPU  -  campo de ondas em ponto flutuante", "FPU  -  floating-point wave field",
            "Calculo de ondas com seno/cosseno em todos os nucleos; mostra GFLOPS estimado.", "Wave calculation with sine/cosine on all cores; shows estimated GFLOPS.",
            "RAM  -  escrita e leitura com verificacao", "RAM  -  write and read with verification",
            "Grava e le padroes na memoria e confere cada valor; qualquer diferenca conta como erro.", "Writes and reads patterns in memory and checks every value; any difference counts as an error.",
            "SSD  -  sequencial + 4K aleatorio", "SSD  -  sequential + random 4K",
            "Usa um arquivo temporario no drive escolhido (apagado ao fim). Em 'Ate parar' gera desgaste.", "Uses a temporary file on the chosen drive (deleted at the end). 'Until stopped' causes wear.",
            "Duracao:", "Duration:",
            "Threads:", "Threads:",
            "Usar (% da livre):", "Use (% of free):",
            "Drive:", "Drive:",
            "Tamanho:", "Size:",
            "Parar se passar de", "Stop if above",
            "Rodar sequencia (CPU, FPU, RAM, GPU)", "Run sequence (CPU, FPU, RAM, GPU)",
            "Parar tudo", "Stop all",
            "Clique no icone de um teste para iniciar.", "Click a test icon to start.",
            "Resultado ao vivo", "Live result",
            "Taxa atual", "Current rate",
            "Teste", "Test",
            "Dur.", "Dur.",
            "Media", "Average",
            "Pico", "Peak",
            "Erros", "Errors",
            "T.max", "Max T",
            "Estab.", "Stab.",
            "vs ant.", "vs prev.",
            "Copiar", "Copy",
            "Salvar CSV", "Save CSV",
            "1. Escolha o programa que quer remover", "1. Choose the program to remove",
            "2. Restos encontrados (revise e remova)", "2. Leftovers found (review and remove)",
            "Buscar:", "Search:",
            "Atualizar lista", "Refresh list",
            "Escolher pasta ou .exe...", "Choose folder or .exe...",
            "Desinstalar e limpar restos", "Uninstall and clean leftovers",
            "Remocao forcada", "Forced removal",
            "So procurar restos", "Only scan for leftovers",
            "Encerrar processos", "End processes",
            "Duplo clique desinstala.", "Double-click to uninstall.",
            "Programa", "Program",
            "Editora", "Publisher",
            "Versao", "Version",
            "Tamanho", "Size",
            "Instalado", "Installed",
            "Tipo", "Type",
            "Local", "Location",
            "Certeza", "Certainty",
            "Marcar tudo", "Check all",
            "Desmarcar tudo", "Uncheck all",
            "Remover itens marcados", "Remove checked items",
            "Pastas e arquivos vao para a Lixeira (podem ser recuperados)", "Folders and files go to the Recycle Bin (recoverable)",
            "Salvar copia (.reg) das chaves do registro antes de apagar", "Save a copy (.reg) of registry keys before deleting",
            "Criar ponto de restauracao do Windows antes", "Create a Windows restore point first",
            "Dica: itens com 'Certeza: alta' sao ligados ao programa; os de certeza 'media' vem desmarcados - confira antes.", "Tip: items with 'high' certainty belong to the program; 'medium' ones come unchecked - review them first.",
            "Pasta", "Folder",
            "Exe", "Exe",
            "Cancelar", "Cancel",
            "Cancelando...", "Cancelling...",
            "Teste de velocidade da internet", "Internet speed test",
            "Informacoes da rede", "Network information",
            "Iniciar teste", "Start test",
            "Mostrar informacoes da rede", "Show network information",
            "Ocultar", "Hide",
            "Mede ping, download e upload de verdade (servidor da Cloudflare).", "Measures real ping, download and upload (Cloudflare server).",
            "Por privacidade, nada e mostrado ate voce clicar. Ao clicar aparecem dados pessoais (IPs, MAC, provedor, cidade, nome do Wi-Fi).", "For privacy, nothing is shown until you click. Clicking reveals personal data (IPs, MAC, ISP, city, Wi-Fi name).",
            "Seus discos", "Your disks",
            "Detalhes do disco selecionado", "Selected disk details",
            "Saude geral:", "Overall health:",
            "Vida util restante:", "Remaining life:",
            "Temperatura:", "Temperature:",
            "Selecione um disco na lista acima.", "Select a disk in the list above.",
            "Atualizar", "Refresh",
            "Disco", "Disk",
            "Saude", "Health",
            "Vida", "Life",
            "Temp.", "Temp.",
            "Horas", "Hours",
            "Atributo / sensor", "Attribute / sensor",
            "Valor", "Value",
            "Resumo da memória", "Memory summary",
            "Timings atuais (leitura automática)", "Current timings (automatic reading)",
            "Lendo os timings...", "Reading timings...",
            "Ler novamente", "Read again",
            "Diagnostico", "Diagnostics",
            "Indicar CPU-Z...", "Set CPU-Z...",
            "Baixar CPU-Z", "Download CPU-Z",
            "Latencia da memoria (ns)", "Memory latency (ns)",
            "Medir latencia", "Measure latency",
            "Parar", "Stop",
            "Latencia da RAM (bloco de 256 MB)", "RAM latency (256 MB block)",
            "Afinidade (CPUs)", "Affinity (CPUs)",
            "Prioridade", "Priority",
            "Prioridade:", "Priority:",
            "Processo", "Process",
            "Memoria", "Memory",
            "Filtrar:", "Filter:",
            "Marcar mesmo nome", "Check same name",
            "Desmarcar", "Uncheck",
            "Pausar atualizacao", "Pause updating",
            "Aplicar", "Apply",
            "Aplicar aos marcados", "Apply to checked",
            "Aplicar regras automaticamente", "Apply rules automatically",
            "Regras automaticas", "Automatic rules",
            "Presets", "Presets",
            "Preset", "Preset",
            "Salvar atual", "Save current",
            "Excluir", "Delete",
            "Nova regra", "New rule",
            "Remover", "Remove",
            "Especificacoes da CPU", "CPU specifications",
            "Especificacoes da GPU selecionada", "Selected GPU specifications",
            "Propriedade", "Property",
            "Fechar", "Close",
            "Todos", "All",
            "Nenhum", "None",
            "Inverter", "Invert",
            "Fisicos", "Physical",
            "Essenciais", "Essentials",
            "1a metade", "1st half",
            "2a metade", "2nd half",
            "1) Ventoinhas detectadas (Ctrl/Shift = varias)", "1) Detected fans (Ctrl/Shift = several)",
            "2) Como as selecionadas devem trabalhar?", "2) How should the selected fans work?",
            "3) Aplicar", "3) Apply",
            "Aplicar nas selecionadas", "Apply to selected",
            "Tudo automatico", "All automatic",
            "Identificar", "Identify",
            "Voltar tudo ao automatico ao fechar", "Return everything to automatic on close",
            "Ventoinha", "Fan",
            "Modo", "Mode",
            "Fabricante", "Manufacturer",
            "Buscar (Ctrl+F):", "Search (Ctrl+F):",
            "Janela:", "Window:",
            "Zerar min/max", "Reset min/max",
            "Copiar tudo", "Copy all",
            "Exportar...", "Export...",
            "Sensor", "Sensor",
            "Min", "Min",
            "Max", "Max",
            "Origem", "Source",
            "Redefinir tudo", "Reset all",
            "Restaurar padrao", "Restore default",
            "Destravar tudo", "Unlock all",
            "Travar", "Lock",
            "Overclock / limites", "Overclock / limits",
            "Manual - velocidade fixa em %:", "Manual - fixed speed in %:",
            "Definir por RPM", "Set by RPM",
            "RPM alvo:", "Target RPM:",
            "Automatico - a placa/BIOS decide (recomendado no dia a dia)", "Automatic - the card/BIOS decides (recommended for daily use)",
            "Travar clock do nucleo (MHz):  min", "Lock core clock (MHz):  min",
            "Travar clock da memoria (MHz):  min", "Lock memory clock (MHz):  min",
            "Atualizar a cada:", "Update every:" };
        static readonly string[] Pairs2 = {
            "  Busca falhou (", "  Search failed (",
            "Windows Update", "Windows Update",
            "servidor padrao", "default server",
            "Nao foi possivel consultar o Windows Update: ", "Could not query Windows Update: ",
            "  O servico Windows Update esta desativado; ativando para esta operacao...", "  The Windows Update service is disabled; enabling it for this operation...",
            "O servico Windows Update esta desativado.", "The Windows Update service is disabled.",
            "Sem acesso ao servidor do Windows Update (internet, DNS, proxy ou firewall).", "No access to the Windows Update server (internet, DNS, proxy or firewall).",
            "O servico Windows Update nao esta pronto; reinicie o Windows e tente de novo.", "The Windows Update service is not ready; restart Windows and try again.",
            "Acesso negado; execute o programa como administrador.", "Access denied; run the program as administrator.",
            "O servidor do Windows Update esta ocupado; tente em alguns minutos.", "The Windows Update server is busy; try again in a few minutes.",
            "Erro: ", "Error: ",
            "Continuar?", "Continue?",
            "Atualizar drivers de todos os dispositivos", "Update drivers for all devices",
            "Analisa todos os dispositivos do PC e procura no Windows Update os drivers mais novos. Antes de instalar, cria um ponto de restauracao e confere a assinatura digital e o hash SHA-256 dos arquivos baixados; se algum estiver alterado, nada e instalado.", "Scans every device in the PC and looks for newer drivers on Windows Update. Before installing, it creates a restore point and checks the digital signature and SHA-256 hash of the downloaded files; if any is altered, nothing is installed.",
            "Atualizar drivers", "Update drivers",
            "So verificar", "Check only",
            "Clique em 'Atualizar drivers' para analisar e instalar, ou em 'So verificar' para apenas listar o que esta desatualizado.", "Click 'Update drivers' to scan and install, or 'Check only' to just list what is outdated.",
            "1/6  Analisando os dispositivos do sistema...", "1/6  Scanning the system's devices...",
            "2/6  Procurando drivers mais novos no Windows Update (pode levar alguns minutos)...", "2/6  Looking for newer drivers on Windows Update (may take a few minutes)...",
            "3/6  Criando ponto de restauracao do sistema...", "3/6  Creating a system restore point...",
            "4/6  Baixando os drivers...", "4/6  Downloading the drivers...",
            "5/6  Verificando assinatura digital e hash SHA-256 dos arquivos baixados...", "5/6  Checking digital signature and SHA-256 hash of the downloaded files...",
            "6/6  Instalando...", "6/6  Installing...",
            "Dispositivos com driver: ", "Devices with a driver: ",
            "   sem assinatura: ", "   unsigned: ",
            "   com problema: ", "   with problems: ",
            "  Sem assinatura: ", "  Unsigned: ",
            "  Com problema (codigo ", "  Problem (code ",
            "  Ponto de restauracao criado.", "  Restore point created.",
            "  Nao foi possivel criar o ponto de restauracao (", "  Could not create the restore point (",
            "). Continuando.", "). Continuing.",
            "Encontrados: ", "Found: ",
            " driver(s) mais novo(s).", " newer driver(s).",
            "Todos os drivers ja estao na versao mais nova oferecida pelo Windows Update.", "All drivers are already at the newest version offered by Windows Update.",
            "Verificacao concluida: ", "Check finished: ",
            " driver(s) podem ser atualizados.", " driver(s) can be updated.",
            "Cancelado pelo usuario. Nada foi instalado.", "Cancelled by the user. Nothing was installed.",
            "Falha no download (codigo ", "Download failed (code ",
            "). Nada foi instalado.", "). Nothing was installed.",
            "  Assinaturas validas: ", "  Valid signatures: ",
            "   sem assinatura propria (verificados pelo Windows Update): ", "   without their own signature (verified by Windows Update): ",
            "   alterados/nao confiaveis: ", "   altered/untrusted: ",
            "ABORTADO: ", "ABORTED: ",
            " arquivo(s) com hash/assinatura invalidos. Nada foi instalado.", " file(s) with invalid hash/signature. Nothing was installed.",
            "  Instalado: ", "  Installed: ",
            "  FALHOU (codigo ", "  FAILED (code ",
            "Concluido: ", "Done: ",
            " instalado(s), ", " installed, ",
            " com falha.", " failed.",
            " REINICIE o Windows para terminar.", " RESTART Windows to finish.",
            "Estes drivers serao baixados, verificados (assinatura e hash SHA-256) e instalados:", "These drivers will be downloaded, verified (signature and SHA-256 hash) and installed:",
            "Sera criado um ponto de restauracao antes. A tela pode piscar (driver de video) e pode ser preciso reiniciar o Windows.", "A restore point will be created first. The screen may flicker (video driver) and Windows may need to restart.",
            "Abrir BunoTunner", "Open BunoTunner",
            "Botao - (minimizar) envia para a bandeja", "The - (minimize) button sends it to the tray",
            "Continua rodando na bandeja. Clique duas vezes no icone para abrir.", "Still running in the tray. Double-click the icon to open.",
            "Sair", "Exit",
            "Placa:", "Card:",
            "Controle e seguranca", "Control and safety",
            "A placa e a escolhida la em cima. Se o OpenGL abrir em outra, o teste muda para OpenCL na escolhida.", "The card is the one chosen at the top. If OpenGL opens on another one, the test switches to OpenCL on the chosen card.",
            "O OpenGL abriu na placa '", "OpenGL opened on card '",
            "', diferente da selecionada ('", "', different from the selected one ('",
            "'): usando o teste OpenCL na placa selecionada.", "'): using the OpenCL test on the selected card.",
            "Erros: ", "Errors: ",
            "FPS do render: ", "Render FPS: ",
            "Pontos: ", "Score: ",
            "Tempo: ", "Time: ",
            " (restam ", " (remaining ",
            "  Velocidade do link: ", "  Link speed: ",
            "  blocos: ", "  blocks: ",
            "  mascara ", "  mask ",
            "  tempo: ", "  time: ",
            "  ultimo: ", "  last: ",
            "  (estimado)", "  (estimated)",
            "  ATENCAO: ", "  WARNING: ",
            "  AVISO: overclock pode causar travamentos e artefatos. Va aos poucos e teste a estabilidade.\n  Vale ate reiniciar ou clicar em Redefinir. Funciona em NVIDIA, AMD (Overdrive8) e Intel Arc.", "  WARNING: overclocking can cause crashes and artifacts. Go in small steps and test stability.\n  Lasts until reboot or until you click Reset. Works on NVIDIA, AMD (Overdrive8) and Intel Arc.",
            "  CEP: ", "  ZIP: ",
            "  Pontos CPU: ", "  CPU score: ",
            "  Proximo teste em instantes...", "  Next test in a moment...",
            "  Sequencia concluida.", "  Sequence finished.",
            "  [driver le ", "  [driver reads ",
            "  nenhum tipo PawnIO na DLL (DLL antiga: atualize a LibreHardwareMonitorLib.dll)", "  no PawnIO type in the DLL (old DLL: update LibreHardwareMonitorLib.dll)",
            "  recurso ", "  feature ",
            "  tipo ", "  type ",
            "  |  Dica: em notebooks o fabricante (BIOS/vBIOS) pode bloquear o overclock; atualize o driver NVIDIA e tente valores menores. A secao \"Travar clock\" costuma funcionar mesmo assim.", "  |  Tip: on laptops the manufacturer (BIOS/vBIOS) may block overclocking; update the NVIDIA driver and try smaller values. The \"Lock clock\" section usually works anyway.",
            "  |  pelos OpenGL", "  |  OpenGL fur",
            "  |  shader + pelos OpenGL", "  |  shader + OpenGL fur",
            " (BIOS legado / CSM)", " (legacy BIOS / CSM)",
            " (BIOS legado ou sem suporte)", " (legacy BIOS or unsupported)",
            " (Esc fecha e para)", " (Esc closes and stops)",
            " (Polaris/Vega/antiga): ajuste de clocks so no AMD Adrenalin; a ventoinha funciona se o driver permitir", " (Polaris/Vega/older): clock tuning only in AMD Adrenalin; the fan works if the driver allows it",
            " (Windows/driver sem suporte)", " (unsupported Windows/driver)",
            " (agora / maximo)", " (now / max)",
            " (aguardando reinicio)", " (waiting for reboot)",
            " (confira se o ajuste manual esta liberado no AMD Adrenalin)", " (check that manual tuning is enabled in AMD Adrenalin)",
            " (em execucao)", " (running)",
            " (erro ", " (error ",
            " (limite ", " (limit ",
            " (sem TPM ou desativado no firmware)", " (no TPM or disabled in firmware)",
            " (sequencia cancelada)", " (sequence cancelled)",
            " (so leitura)", " (read-only)",
            " CPUs marcados", " CPUs checked",
            " Copias do registro em BunoTunner_backup.", " Registry copies in BunoTunner_backup.",
            " MB liberados.", " MB freed.",
            " MB/s | 4K aleatorio ", " MB/s | random 4K ",
            " MB/s | Leitura seq ", " MB/s | Seq. read ",
            " MHz     Memoria: ", " MHz     Memory: ",
            " MHz reais", " real MHz",
            " adaptador(es):", " adapter(s):",
            " ainda nao foi configurado.\nEm breve!", " has not been set up yet.\nComing soon!",
            " ajustada(s).", " adjusted.",
            " alerta(s)", " alert(s)",
            " arquivo(s) apagado(s), ", " file(s) deleted, ",
            " arquivo(s) em uso serao apagados no proximo reinicio do Windows.", " file(s) in use will be deleted on the next Windows restart.",
            " canais", " channels",
            " canal(ais)).  ", " channel(s)).  ",
            " canal(is) em ", " channel(s) at ",
            " com falha.", " failed.",
            " dias)", " days)",
            " disco(s). Alguns discos USB/RAID nao informam S.M.A.R.T. ao Windows.", " disk(s). Some USB/RAID disks do not report S.M.A.R.T. to Windows.",
            " do sistema ignorado(s)", " system item(s) skipped",
            " durante o calculo (o driver pode ter reiniciado).", " during the calculation (the driver may have restarted).",
            " e o CPU-Z não foi encontrado", " and CPU-Z was not found",
            " e o CPU-Z não retornou dados", " and CPU-Z returned no data",
            " erro 0x", " error 0x",
            " erro(s) de calculo/verificacao - sistema instavel!", " calculation/verification error(s) - unstable system!",
            " falhou (", " failed (",
            " item(ns) encontrado(s). Revise a lista (so os de certeza alta vem marcados) e clique em 'Remover itens marcados'.", " item(s) found. Review the list (only high-certainty ones come checked) and click 'Remove checked items'.",
            " item(ns) marcado(s)?\n\n", " checked item(s)?\n\n",
            " item(ns) removido(s)", " item(s) removed",
            " módulo(s))", " module(s))",
            " no grafico  |  ", " on the graph  |  ",
            " nucleos, ", " cores, ",
            " processo(s) alterado(s)", " process(es) changed",
            " processo(s) encerrado(s). Procurando restos...", " process(es) ended. Looking for leftovers...",
            " processo(s) finalizado(s).", " process(es) ended.",
            " programas. Escolha um na lista.", " programs. Choose one from the list.",
            " rank(s) no total", " rank(s) in total",
            " s   melhor: ", " s   best: ",
            " sem RPM maximo medido: use \"Identificar\" antes.", " no measured max RPM: use \"Identify\" first.",
            " sensores  |  ", " sensors  |  ",
            " sensores)", " sensors)",
            " ventoinha(s) aplicada(s). ", " fan(s) applied. ",
            " ventoinha(s) do tipo \"", " fan(s) of type \"",
            "% (a placa pode subir se esquentar)", "% (the card may go higher if it heats up)",
            "% restante", "% remaining",
            "(!) Os sensores da CPU nao estao sendo lidos pelo driver (temperatura/tensao/consumo). Clique aqui para ver como resolver.", "(!) CPU sensors are not being read by the driver (temperature/voltage/power). Click here to see how to fix it.",
            "(indisponivel)", "(unavailable)",
            "(interrompido)", "(interrupted)",
            "(negado)", "(denied)",
            "(use \"Identificar\" antes: ele mede o RPM maximo de cada ventoinha)", "(use \"Identify\" first: it measures each fan's max RPM)",
            ") não expõe os timings sem um driver próprio", ") does not expose timings without its own driver",
            ").\nAbrir o download no navegador?", ").\nOpen the download in the browser?",
            "): usando o teste OpenCL.", "): using the OpenCL test.",
            ")?\nTrabalho nao salvo nele sera perdido.", ")?\nUnsaved work in it will be lost.",
            "- Botao direito: criar ALERTA (som), copiar nome/valor, zerar min/max.\n", "- Right-click: create an ALERT (sound), copy name/value, reset min/max.\n",
            "- Cartoes no topo: os valores principais (GPU, CPU, RAM, ventoinha) sempre a vista.\n", "- Cards on top: the main values (GPU, CPU, RAM, fan) always in view.\n",
            "- Exportar salva os valores/historico em CSV ou o grafico em PNG.", "- Export saves the values/history as CSV or the graph as PNG.",
            "- Grafico: passe o mouse para ver os valores; \"Normalizar escala\" coloca unidades diferentes lado a lado.\n", "- Graph: hover to see the values; \"Normalize scale\" puts different units side by side.\n",
            "- Lista: escolha a categoria a esquerda (GPU, CPU, Placa-mae, Memoria...) e use a busca (Ctrl+F) e o filtro de tipo.\n", "- List: choose the category on the left (GPU, CPU, Motherboard, Memory...) and use search (Ctrl+F) and the type filter.\n",
            "- Marque a caixinha (ou de duplo clique) para ver o sensor no grafico.\n", "- Tick the checkbox (or double-click) to see the sensor on the graph.\n",
            "- \"Atualizar a cada\" muda a velocidade das leituras (em milissegundos).\n", "- \"Update every\" changes the reading speed (in milliseconds).\n",
            "- \"Essenciais\" marca os sensores mais importantes de uma vez.\n", "- \"Essentials\" ticks the most important sensors at once.\n",
            "--- Adaptador: ", "--- Adapter: ",
            "--- Ao vivo ---", "--- Live ---",
            "--- Overclock / controle (", "--- Overclock / control (",
            "--- Recursos (ligado / desligado) ---", "--- Features (on / off) ---",
            "--- Recursos e seguranca (ligado / desligado) ---", "--- Features and security (on / off) ---",
            ". Alternativa: baixe o CPU-Z (versão ZIP), extraia ao lado do programa ou use \"Indicar CPU-Z\"; o programa o usa só para ler os timings.", ". Alternative: download CPU-Z (ZIP version), extract it next to the program or use \"Set CPU-Z\"; the program only uses it to read the timings.",
            ". Esta placa/driver nao aceitou controle manual.", ". This card/driver did not accept manual control.",
            ". Use 'Remocao forcada' ou 'So procurar restos'.", ". Use 'Forced removal' or 'Only scan for leftovers'.",
            "2a thread", "2nd thread",
            "1) \"Integridade de memoria\" (Isolamento de nucleo) do Windows ou o Defender bloqueando o driver: desative em Seguranca do Windows > Seguranca do dispositivo > Isolamento de nucleo e reinicie o PC.\n", "1) Windows \"Memory integrity\" (Core isolation) or Defender blocking the driver: turn it off in Windows Security > Device security > Core isolation and restart the PC.\n",
            "2) Versao do LibreHardwareMonitor antiga para a sua CPU: use a mais recente (versoes novas podem exigir o driver PawnIO).\n", "2) LibreHardwareMonitor version too old for your CPU: use the latest one (newer versions may require the PawnIO driver).\n",
            "3) Antivirus de terceiros.\n\n", "3) Third-party antivirus.\n\n",
            "; rode como administrador); ", "; run as administrator); ",
            "=== Este computador ===", "=== This computer ===",
            "=== Internet (visto de fora) ===", "=== Internet (seen from outside) ===",
            "?  Como usar", "?  How to use",
            "A placa Intel nao expoe controle de ventoinha.", "The Intel card does not expose fan control.",
            "A placa-mae pode nao expor as ventoinhas ao Windows", "The motherboard may not expose the fans to Windows",
            "A propria placa-mae ou o driver controla a velocidade pela temperatura.", "The motherboard or driver itself controls the speed based on temperature.",
            "A ventoinha fica sempre na mesma velocidade, nao importa a temperatura.", "The fan always stays at the same speed, no matter the temperature.",
            "ADL incompleta", "ADL incomplete",
            "ADL sem Overdrive8", "ADL without Overdrive8",
            "ADL sem Overdrive8_Setting_Set", "ADL without Overdrive8_Setting_Set",
            "ADL2_Main_Control_Create erro ", "ADL2_Main_Control_Create error ",
            "ALERTA: ", "ALERT: ",
            "AMD (plataforma nao identificada)", "AMD (platform not identified)",
            "ATENCAO: as informacoes a seguir sao PESSOAIS.\n\nVao aparecer seu IP interno e externo, enderecos MAC, provedor, cidade aproximada, nome da rede Wi-Fi e DNS.\nNao compartilhe prints desta tela.\n\nMostrar mesmo assim?", "WARNING: the following information is PERSONAL.\n\nYour internal and external IP, MAC addresses, ISP, approximate city, Wi-Fi network name and DNS will appear.\nDo not share screenshots of this screen.\n\nShow it anyway?",
            "ATENCAO: desativar as mitigacoes de Spectre/Meltdown deixa o PC mais vulneravel a ataques que leem memoria (por exemplo, por codigo malicioso no navegador). O ganho de desempenho costuma ser pequeno em CPUs recentes.\n\nDesativar mesmo assim?", "WARNING: disabling Spectre/Meltdown mitigations makes the PC more vulnerable to attacks that read memory (for example, malicious code in the browser). The performance gain is usually small on recent CPUs.\n\nDisable anyway?",
            "ATENCAO: o disco indica falha iminente ou pouca vida util. Faca backup AGORA e planeje a troca.", "WARNING: the disk reports imminent failure or little remaining life. Back up NOW and plan a replacement.",
            "ATENCAO: ", "WARNING: ",
            "Abaixo do normal", "Below normal",
            "Aceleracao de GPU por hardware (HAGS)", "Hardware-accelerated GPU scheduling (HAGS)",
            "Aceleracao do mouse ATIVADA.", "Mouse acceleration ENABLED.",
            "Aceleracao do mouse DESATIVADA.", "Mouse acceleration DISABLED.",
            "Acima do normal", "Above normal",
            "AdapterInfo_Get falhou (", "AdapterInfo_Get failed (",
            "Adicionado: ", "Added: ",
            "Administrador: ", "Administrator: ",
            "Alerta definido: ", "Alert set: ",
            "Alerta removido.", "Alert removed.",
            "Alerta", "Alert",
            "Alta", "High",
            "Aplicar tambem a TODOS os processos com o mesmo nome", "Also apply to ALL processes with the same name",
            "Apoie o BunoTunner via LivePix", "Support BunoTunner via LivePix",
            "Apoie o BunoTunner via PayPal", "Support BunoTunner via PayPal",
            "Armazenamento (HD/SSD)", "Storage (HDD/SSD)",
            "Armazenamento", "Storage",
            "Arquitetura", "Architecture",
            "Arquivo", "File",
            "Atalho", "Shortcut",
            "Ate parar", "Until stopped",
            "Atencao", "Attention",
            "Automatico", "Automatic",
            "Avisar (som + barra de status) quando \"", "Notify (sound + status bar) when \"",
            "Aviso", "Warning",
            "Baixa (Idle)", "Low (Idle)",
            "Baixar e instalar o PawnIO agora?\n\nO instalador oficial (github.com/namazso/PawnIO.Setup) sera baixado e pedira permissao de administrador.", "Download and install PawnIO now?\n\nThe official installer (github.com/namazso/PawnIO.Setup) will be downloaded and will ask for administrator permission.",
            "Banda teórica: ", "Theoretical bandwidth: ",
            "Barramento da memoria", "Memory bus",
            "Bateria", "Battery",
            "Boa", "Good",
            "Bomba/AIO", "Pump/AIO",
            "Boot em modo UEFI (necessario p/ ReBAR)", "UEFI boot mode (required for ReBAR)",
            "Boot em modo UEFI", "UEFI boot mode",
            "BunoTunner - Adicionar manualmente", "BunoTunner - Add manually",
            "BunoTunner - Dados pessoais", "BunoTunner - Personal data",
            "BunoTunner - Desinstalar", "BunoTunner - Uninstall",
            "BunoTunner - Otimizacoes", "BunoTunner - Optimizations",
            "BunoTunner - PawnIO recomendado", "BunoTunner - PawnIO recommended",
            "BunoTunner - Remocao forcada", "BunoTunner - Forced removal",
            "BunoTunner - Remover", "BunoTunner - Remove",
            "BunoTunner - Render do teste (Esc fecha e para)", "BunoTunner - Test render (Esc closes and stops)",
            "BunoTunner - Sensores (GPU, CPU, placa-mae, memoria, discos, ventoinhas...)", "BunoTunner - Sensors (GPU, CPU, motherboard, memory, disks, fans...)",
            "BunoTunner - Sensores", "BunoTunner - Sensors",
            "BunoTunner - diagnostico dos timings", "BunoTunner - timings diagnostics",
            "BunoTunner - erro fatal", "BunoTunner - fatal error",
            "BunoTunner - tempos da abertura (cada linha: tempo total, tempo desta etapa)\r\n", "BunoTunner - startup timings (each line: total time, time of this step)\r\n",
            "COMO USAR OS SENSORES\n\n", "HOW TO USE THE SENSORS\n\n",
            "CPU - consumo", "CPU - power",
            "CPU - render 3D por blocos (ray tracing)", "CPU - block-based 3D render (ray tracing)",
            "CPU - temperatura", "CPU - temperature",
            "CPU - uso", "CPU - usage",
            "Programas (*.exe)|*.exe|Todos|*.*", "Programs (*.exe)|*.exe|All|*.*",
            "CPU-Z (cpuz*.exe)|cpuz*.exe|Programas|*.exe", "CPU-Z (cpuz*.exe)|cpuz*.exe|Programs|*.exe",
            "Canais:", "Channels:",
            "Cancelado.", "Cancelled.",
            "Cancelado", "Cancelled",
            "Capacidade total:", "Total capacity:",
            "Capacidade", "Capacity",
            "Carregando sensores e hardware...", "Loading sensors and hardware...",
            "Causas comuns:\n", "Common causes:\n",
            "Ciclos liga/desliga", "Power cycles",
            "Cidade: ", "City: ",
            "Clock atual (WMI)", "Current clock (WMI)",
            "Clock base", "Base clock",
            "Clock da memoria", "Memory clock",
            "Clock de video", "Video clock",
            "Clock do nucleo", "Core clock",
            "Clock externo (BCLK)", "External clock (BCLK)",
            "Clock maximo", "Max clock",
            "Clock medio dos nucleos", "Average core clock",
            "Clock travado.", "Clock locked.",
            "Clocks atuais e suporte desta placa", "Current clocks and support of this card",
            "Clocks destravados.", "Clocks unlocked.",
            " (agora)", " (now)",
            "Codigo ", "Code ",
            "Coletando informacoes...", "Collecting information...",
            "Compressao de memoria desativada. Reinicie o Windows para valer.", "Memory compression disabled. Restart Windows for it to take effect.",
            "Compressao de memoria reativada. Reinicie o Windows para valer.", "Memory compression re-enabled. Restart Windows for it to take effect.",
            "Concluido: ", "Done: ",
            "Concluido.", "Done.",
            "Concluido", "Done",
            "Conjuntos de instrucoes", "Instruction sets",
            "Consumo (watts)", "Power (watts)",
            "Consumo do pacote", "Package power",
            "Continuar", "Continue",
            "ControlLib.dll nao encontrada (instale o driver Intel Arc / Graphics mais recente)", "ControlLib.dll not found (install the latest Intel Arc / Graphics driver)",
            "Controlador embarcado", "Embedded controller",
            "Controladora", "Controller",
            "Controle (%)", "Control (%)",
            "Controle automatico (curva original da placa) restaurado.", "Automatic control (card's original curve) restored.",
            "Controle da ventoinha", "Fan control",
            "Controle de ventoinha indisponivel.", "Fan control unavailable.",
            "Copiado.", "Copied.",
            "Copiar nome", "Copy name",
            "Copiar valor", "Copy value",
            "Corrente", "Current",
            "Criando ponto de restauracao (pode levar um minuto)...", "Creating restore point (may take a minute)...",
            "Crossbar / Uncore / Fabric clock: nenhuma API publica (NVML, NvAPI, ADL, IGCL) expoe esses clocks, entao nenhum programa de terceiros consegue altera-los.", "Crossbar / Uncore / Fabric clock: no public API (NVML, NvAPI, ADL, IGCL) exposes these clocks, so no third-party program can change them.",
            "DDR5: cada módulo tem 2 subcanais de 32 bits.", "DDR5: each module has two 32-bit sub-channels.",
            "DEP / NX (protecao de execucao)", "DEP / NX (execution protection)",
            "Data do driver (registro)", "Driver date (registry)",
            "Data do driver", "Driver date",
            "Definir alerta (acima de...)", "Set alert (above...)",
            "Depois do download, execute o PawnIO_setup.exe (como administrador), conclua a instalacao e abra o BunoTunner novamente.", "After downloading, run PawnIO_setup.exe (as administrator), finish the installation and open BunoTunner again.",
            "Desativar o Isolamento de nucleo reduz a protecao contra malware que ataca o nucleo do Windows. Desativar mesmo assim?", "Disabling Core isolation reduces protection against malware that attacks the Windows kernel. Disable anyway?",
            "Desconhecida", "Unknown",
            "Descricao: ", "Description: ",
            "Deseja abrir o download oficial agora?\n(Sim = baixa o PawnIO_setup.exe; Nao = continuar sem ele. Este aviso aparece a cada abertura enquanto ele nao estiver instalado.)", "Do you want to open the official download now?\n(Yes = downloads PawnIO_setup.exe; No = continue without it. This notice appears at every start while it is not installed.)",
            "Desgaste (uso da vida util)", "Wear (life used)",
            "Desinstalador aberto: conclua as telas dele... (a busca de restos comeca quando ele terminar)", "Uninstaller open: finish its screens... (the leftover search starts when it ends)",
            "Desinstalador terminou.", "Uninstaller finished.",
            "Desinstalar \"", "Uninstall \"",
            "Desligado (1 thread por nucleo)", "Off (1 thread per core)",
            "Desligado ou nao confirmado (BAR1 de 256 MB)", "Off or unconfirmed (256 MB BAR1)",
            "Desligado", "Off",
            "Dica: marque a caixinha (ou de duplo clique) para ver o sensor no grafico. Botao direito = alerta, copiar... Clique no cabecalho para ordenar.", "Tip: tick the checkbox (or double-click) to see the sensor on the graph. Right-click = alert, copy... Click the header to sort.",
            "Digite parte do nome. Varias palavras = todas precisam aparecer (ex.: core temp). Ctrl+A seleciona tudo.", "Type part of the name. Several words = all must appear (e.g. core temp). Ctrl+A selects all.",
            "Discos", "Disks",
            "Driver SEM leitura da CPU (Integridade de memoria/Defender?) - usando zona termica do Windows", "Driver WITHOUT CPU reading (Memory integrity/Defender?) - using Windows thermal zone",
            "Duracao", "Duration",
            "ECC da memoria", "Memory ECC",
            "Em 'Ate parar' o SSD recebe gravacoes continuas (gera desgaste). Continuar?", "With 'Until stopped' the SSD receives continuous writes (causes wear). Continue?",
            "Enquanto isso o programa usa as zonas termicas do Windows (menos precisas).\n\nAbrir agora a tela de Isolamento de nucleo?", "Meanwhile the program uses Windows thermal zones (less accurate).\n\nOpen the Core isolation screen now?",
            "Erro ao executar ", "Error running ",
            "Erro de escrita (", "Write error (",
            "Erro de leitura (", "Read error (",
            "Erro na busca: ", "Search error: ",
            "Erro: ", "Error: ",
            "Erros de escrita (total)", "Write errors (total)",
            "Erros de leitura (total)", "Read errors (total)",
            "Erros:", "Errors:",
            "Escolha (ou salve) um preset antes de criar a regra.", "Choose (or save) a preset before creating the rule.",
            "Escolha a pasta do programa", "Choose the program folder",
            "Escolha o cpuz.exe (versao portatil ou instalada)", "Choose cpuz.exe (portable or installed version)",
            "Escolha o que voce quer ver: GPU, CPU, placa-mae, memoria, discos, ventoinhas...", "Choose what you want to see: GPU, CPU, motherboard, memory, disks, fans...",
            "Escolha um preset para carregar nucleos e prioridade; depois clique em \"Aplicar aos marcados\". \"Salvar atual\" guarda o que esta na grade.", "Choose a preset to load cores and priority; then click \"Apply to checked\". \"Save current\" stores what is in the grid.",
            "Escrita seq ", "Seq. write ",
            "Essa pasta e protegida pelo sistema e nao pode ser usada aqui:\n", "This folder is protected by the system and cannot be used here:\n",
            "Esta placa AMD nao aceitou controle de ventoinha pelo ADL.", "This AMD card did not accept fan control through ADL.",
            "Esta placa expoe todos os ajustes acima. ", "This card exposes all the settings above. ",
            "Esta ventoinha e somente leitura.", "This fan is read-only.",
            "Estabilidade", "Stability",
            "Estado de desempenho", "Performance state",
            "Estado informado pelo Windows", "State reported by Windows",
            "Estado: ATIVADA", "State: ENABLED",
            "Estado: ATIVADO", "State: ENABLED",
            "Estado: DESATIVADA", "State: DISABLED",
            "Estado: MPO DESATIVADO", "State: MPO DISABLED",
            "Estado: MPO padrao (ativo)", "State: MPO default (active)",
            "Estado: ativada", "State: enabled",
            "Estado: compressao ATIVADA", "State: compression ENABLED",
            "Estado: compressao DESATIVADA", "State: compression DISABLED",
            "Estado: desativada", "State: disabled",
            "Estado: desativado", "State: disabled",
            "Estado: menu classico ATIVO", "State: classic menu ACTIVE",
            "Estado: menu padrao do Windows 11", "State: Windows 11 default menu",
            "Estado: overlay ATIVO", "State: overlay ACTIVE",
            "Estado: overlay desligado", "State: overlay off",
            "Estado: padrao do Windows (mitigacoes ativas)", "State: Windows default (mitigations active)",
            "Estado: personalizado (FeatureSettingsOverride=", "State: custom (FeatureSettingsOverride=",
            "Este item nao tem desinstalador. Use 'Remocao forcada'.", "This item has no uninstaller. Use 'Forced removal'.",
            "Exclusivo", "Exclusive",
            "Exportar", "Export",
            "FALHOU - ", "FAILED - ",
            "FALHOU", "FAILED",
            "FPU - malha de ondas em ponto flutuante", "FPU - floating-point wave mesh",
            "Faixa da ventoinha (driver)", "Fan range (driver)",
            "Faixa de potencia permitida", "Allowed power range",
            "Falha ao abrir o desinstalador: ", "Failed to open the uninstaller: ",
            "Falha ao salvar: ", "Failed to save: ",
            "Falhou (execute como administrador): ", "Failed (run as administrator): ",
            "Falhou: Overdrive8 indisponivel", "Failed: Overdrive8 unavailable",
            "Falhou: ctlFanSetFixedSpeedMode erro 0x", "Failed: ctlFanSetFixedSpeedMode error 0x",
            "Falhou: funcao ausente", "Failed: function missing",
            "Falhou: ", "Failed: ",
            "Falhou", "Failed",
            "Familia / modelo / stepping", "Family / model / stepping",
            "Ventoinha (RPM agora)", "Fan (RPM now)",
            "Ventoinha (% agora)", "Fan (% now)",
            "Filtrar por tipo de medida (temperatura, uso, clock...)", "Filter by measurement type (temperature, usage, clock...)",
            "Finaliza todos os programas abertos que rodam da pasta do programa selecionado (e para os servicos dele). Use antes de desinstalar se ele estiver aberto.", "Ends all open programs that run from the selected program's folder (and stops its services). Use before uninstalling if it is open.",
            "Finalizando o teste anterior... tente de novo em um instante.", "Ending the previous test... try again in a moment.",
            "Finalizar todos os processos de \"", "End all processes of \"",
            "Fonte: ", "Source: ",
            "Fonte", "Source",
            "Fuso horario: ", "Time zone: ",
            "GPU nao encontrada", "GPU not found",
            "GPU - consumo", "GPU - power",
            "GPU - render 3D de pelos (OpenGL)", "GPU - 3D fur render (OpenGL)",
            "GPU - temperatura", "GPU - temperature",
            "GPU - uso", "GPU - usage",
            "GPUs listadas", "GPUs listed",
            "Gabinete", "Case",
            "Ha sinais de desgaste, erros ou temperatura alta. Mantenha backup em dia e acompanhe.", "There are signs of wear, errors or high temperature. Keep your backups up to date and monitor it.",
            "Hibernacao ATIVADA.", "Hibernation ENABLED.",
            "Hibernacao DESATIVADA (hiberfil.sys removido).", "Hibernation DISABLED (hiberfil.sys removed).",
            "Historico do grafico (CSV)...", "Graph history (CSV)...",
            "Horas ligado", "Power-on hours",
            "Host reverso: ", "Reverse host: ",
            "Hypervisor ativo (Hyper-V / WSL2)", "Hypervisor active (Hyper-V / WSL2)",
            "ID de hardware (PNP)", "Hardware ID (PNP)",
            "ID do processador", "Processor ID",
            "IP externo: ", "External IP: ",
            "IP preferido (v6 se houver): ", "Preferred IP (v6 if available): ",
            "Identificacao concluida: RPM maximo guardado (usado em \"Definir por RPM\").", "Identification finished: max RPM saved (used in \"Set by RPM\").",
            "Identificador", "Identifier",
            "Identificar (100% por 5 s)", "Identify (100% for 5 s)",
            "Identificar = liga as selecionadas a 100% por 5 s para voce descobrir qual e qual.  Botao direito na lista: selecionar por tipo (CPU, bombas, gabinete) ou renomear (inclua CPU, Bomba ou Gabinete no nome).", "Identify = runs the selected fans at 100% for 5 s so you can tell which is which.  Right-click the list: select by type (CPU, pumps, case) or rename (include CPU, Pump or Case in the name).",
            "Idioma:", "Language:",
            "Imagem do grafico (PNG)...", "Graph image (PNG)...",
            "Indisponivel (a leitura exata so existe no driver NVIDIA; veja no AMD Adrenalin: Smart Access Memory, ou na BIOS)", "Unavailable (the exact reading only exists in the NVIDIA driver; check AMD Adrenalin: Smart Access Memory, or the BIOS)",
            "Indisponivel (placa sem ECC)", "Unavailable (board without ECC)",
            "Indisponivel (placa/driver nao expoe)", "Unavailable (board/driver does not expose it)",
            "Indisponivel (", "Unavailable (",
            "Indisponivel: ", "Unavailable: ",
            "Indisponivel", "Unavailable",
            "Informe min e max validos.", "Enter valid min and max.",
            "Inicializacao rapida (Fast Startup)", "Fast Startup",
            "Inicializacao", "Startup",
            "Iniciando...", "Starting...",
            "Iniciar / parar o estresse de CPU", "Start / stop the CPU stress",
            "Iniciar / parar o estresse de FPU", "Start / stop the FPU stress",
            "Iniciar / parar o estresse de GPU", "Start / stop the GPU stress",
            "Iniciar / parar o estresse de RAM", "Start / stop the RAM stress",
            "Iniciar / parar o estresse de SSD", "Start / stop the SSD stress",
            "Instalar PawnIO", "Install PawnIO",
            "Integridade de memoria (HVCI)", "Memory integrity (HVCI)",
            "Intel (soquete nao identificado)", "Intel (socket not identified)",
            "Intel IGCL (experimental): o driver Intel valida os limites; valores fora da faixa sao recusados.", "Intel IGCL (experimental): the Intel driver validates the limits; out-of-range values are rejected.",
            "Interrompido pelo usuario.", "Interrupted by the user.",
            "Isolamento de nucleo ATIVADO. Reinicie o PC.", "Core isolation ENABLED. Restart the PC.",
            "Isolamento de nucleo DESATIVADO. Reinicie o PC.", "Core isolation DISABLED. Restart the PC.",
            "Janela de render fechada: teste interrompido.", "Render window closed: test interrupted.",
            "LHM aberto (CPU, GPU, placa-mae, memoria)", "LHM opened (CPU, GPU, motherboard, memory)",
            "Largura de endereco", "Address width",
            "Latencia da RAM (bloco de ", "RAM latency (block of ",
            "Latência real do CAS: ", "Actual CAS latency: ",
            "Leitura de sensores", "Sensor reading",
            "Leitura dos sensores da CPU", "CPU sensor reading",
            "Leitura", "Reading",
            "Lendo a lista de programas instalados...", "Reading the list of installed programs...",
            "Lendo informacoes da CPU...", "Reading CPU information...",
            "Lendo módulos...", "Reading modules...",
            "Lendo os discos (Windows Storage + S.M.A.R.T.)...", "Reading disks (Windows Storage + S.M.A.R.T.)...",
            "Lendo os timings direto do controlador de memória...", "Reading timings directly from the memory controller...",
            "Ligado (exigido pelo BAR grande)", "On (required by the large BAR)",
            "Ligado: cada linha usa a propria escala (0-100%). Desligado: mesma escala para todas.", "On: each line uses its own scale (0-100%). Off: same scale for all.",
            "Ligado - faixa ", "On - range ",
            "Ligado (", "On (",
            "Ligado", "On",
            "Limitacao de clock agora", "Current clock limit",
            "Limite de potencia (TDP)", "Power limit (TDP)",
            "Limite de potencia atual", "Current power limit",
            "Limite de potencia padrao", "Default power limit",
            "Limite de potencia", "Power limit",
            "Limite de temperatura", "Temperature limit",
            "Limpar historico", "Clear history",
            "Localizacao aproximada: ", "Approximate location: ",
            "MPO desativado. Reinicie o PC.", "MPO disabled. Restart the PC.",
            "MPO restaurado ao padrao. Reinicie o PC.", "MPO restored to default. Restart the PC.",
            "Manter a atual", "Keep current",
            "Marque (caixinha) os processos que quer alterar. Sem nenhum marcado, vale a selecao com Ctrl/Shift.", "Check the processes you want to change. With none checked, the Ctrl/Shift selection is used.",
            "Marque (caixinha) ou selecione pelo menos um processo na lista.", "Check or select at least one process in the list.",
            "Marque pelo menos um CPU antes de salvar.", "Check at least one CPU before saving.",
            "Marque pelo menos um CPU na grade.", "Check at least one CPU in the grid.",
            "Marque sensores na lista (ou use 'Essenciais') para ver o grafico", "Check sensors in the list (or use 'Essentials') to see the graph",
            "Marque sensores para o grafico primeiro.", "Check sensors for the graph first.",
            "Media:", "Average:",
            "Medindo (feche outros programas para um resultado melhor)...", "Measuring (close other programs for a better result)...",
            "Medindo download...", "Measuring download...",
            "Medindo latencia...", "Measuring latency...",
            "Medindo upload...", "Measuring upload...",
            "Memoria RAM", "RAM",
            "Memoria de video (VRAM)", "Video memory (VRAM)",
            "Memoria livre insuficiente para o teste.", "Not enough free memory for the test.",
            "Memoria/Dados", "Memory/Data",
            "Memoria: ", "Memory: ",
            "Menu classico ativado. Reinicie o Explorer ou faca logoff.", "Classic menu enabled. Restart Explorer or sign out.",
            "Menu padrao do Windows 11 restaurado. Reinicie o Explorer ou faca logoff.", "Windows 11 default menu restored. Restart Explorer or sign out.",
            "Metade", "Half",
            "Mitigacoes DESATIVADAS. Reinicie o PC.", "Mitigations DISABLED. Restart the PC.",
            "Mitigacoes restauradas ao padrao do Windows. Reinicie o PC.", "Mitigations restored to the Windows default. Restart the PC.",
            "Mobile (soldada)", "Mobile (soldered)",
            "Modelo", "Model",
            "Modo de computacao", "Compute mode",
            "Modo de jogo (Game Mode)", "Game Mode",
            "Monitor conectado / display ativo", "Connected monitor / active display",
            "Mostrar / ocultar no grafico", "Show / hide on graph",
            "Motivos: ", "Reasons: ",
            "Muda o visual do programa: Luna (XP), Frutiger Aero (Win7), Dark/AMOLED, Neon, Steampunk ou Vintage (Windows 95)", "Changes the program's look: Luna (XP), Frutiger Aero (Win7), Dark/AMOLED, Neon, Steampunk or Vintage (Windows 95)",
            "NAO (rode como administrador)", "NO (run as administrator)",
            "Nada sobrou: nenhum resto encontrado para ", "Nothing left: no leftovers found for ",
            "Nao consegui criar o arquivo de teste em ", "Could not create the test file in ",
            "Nao foi possivel abrir o navegador: ", "Could not open the browser: ",
            "Nao foi possivel consultar o IP externo: ", "Could not look up the external IP: ",
            "Nao foi possivel criar o ponto de restauracao (o Windows limita 1 a cada 24 h, ou a protecao do sistema esta desligada). Continuando.", "Could not create the restore point (Windows limits it to 1 per 24 h, or system protection is off). Continuing.",
            "Nao foi possivel instalar automaticamente (", "Could not install automatically (",
            "Nao foi possivel ler: ", "Could not read: ",
            "Nao foi possivel medir (sem internet ou bloqueio de rede?): ", "Could not measure (no internet or network block?): ",
            "Nao foi possivel remover:\n\n", "Could not remove:\n\n",
            "Nao foi possivel restaurar o modo automatico.", "Could not restore automatic mode.",
            "Nao sei qual e a pasta deste programa; use 'Escolher pasta ou .exe' para informar.", "I don't know this program's folder; use 'Choose folder or .exe' to tell me.",
            "Navegadores e jogos criam varios processos com o mesmo nome; isto altera todos de uma vez.", "Browsers and games create several processes with the same name; this changes all of them at once.",
            "Nenhum ajuste disponivel nesta placa.", "No adjustments available on this card.",
            "Nenhum backend de overclock para esta GPU. ", "No overclock backend for this GPU. ",
            "Nenhum drive disponivel para o teste de SSD.", "No drive available for the SSD test.",
            "Nenhum item marcado.", "No item checked.",
            "Nenhum módulo informado pelo Windows (WMI)", "No module reported by Windows (WMI)",
            "Nenhum processo desse programa esta aberto.", "No process of this program is open.",
            "Nenhum resultado para copiar ainda.", "No result to copy yet.",
            "Nenhum resultado para salvar ainda.", "No result to save yet.",
            "Nenhuma GPU encontrada pelo OpenCL.", "No GPU found by OpenCL.",
            "Nenhuma GPU selecionada.", "No GPU selected.",
            "Nenhuma placa de video foi detectada.\nRode como administrador e confira a pasta libs.", "No graphics card was detected.\nRun as administrator and check the libs folder.",
            "Nenhuma ventoinha controlavel do tipo \"", "No controllable fan of type \"",
            "Nenhuma ventoinha encontrada", "No fan found",
            "Nenhuma", "None",
            "Nome (inclua CPU, Bomba ou Gabinete para definir o tipo):", "Name (include CPU, Pump or Case to set the type):",
            "Nome do PC: ", "PC name: ",
            "Nome do hardware nao identificado.", "Hardware name not identified.",
            "Nome do preset (ex.: Jogo - nucleos fisicos):", "Preset name (e.g. Game - physical cores):",
            "Nome do processo (ex.: game.exe). Preset: ", "Process name (e.g. game.exe). Preset: ",
            "Nome", "Name",
            "Normalizar escala", "Normalize scale",
            "Nucleo: ", "Core: ",
            "Nucleos (CPUs logicos) que os processos marcados podem usar", "Cores (logical CPUs) the checked processes may use",
            "Nucleos CUDA", "CUDA cores",
            "Nucleos fisicos", "Physical cores",
            "NvAPI EnumGPUs erro ", "NvAPI EnumGPUs error ",
            "NvAPI SetPstates20 erro ", "NvAPI SetPstates20 error ",
            "NvAPI indisponivel", "NvAPI unavailable",
            "NvAPI sem SetPstates20", "NvAPI without SetPstates20",
            "NvAPI_Initialize erro ", "NvAPI_Initialize error ",
            "Não consegui ler: ", "Could not read: ",
            "O PawnIO e um driver assinado e de codigo aberto que o LibreHardwareMonitor (usado pelo BunoTunner) precisa para acessar o hardware. ", "PawnIO is a signed, open-source driver that LibreHardwareMonitor (used by BunoTunner) needs to access the hardware. ",
            "O PawnIO ja esta instalado.", "PawnIO is already installed.",
            "O Windows nao informou S.M.A.R.T. deste disco (comum em discos USB e controladoras RAID).", "Windows did not report S.M.A.R.T. for this disk (common on USB disks and RAID controllers).",
            "O instalador foi fechado, mas o PawnIO nao foi detectado. Tente novamente.", "The installer was closed, but PawnIO was not detected. Try again.",
            "O link do ", "The link for ",
            "OC das GPUs (NVML/ADL/IGCL)", "GPU OC (NVML/ADL/IGCL)",
            "OK (ADL Overdrive6): ventoinha em ", "OK (ADL Overdrive6): fan at ",
            "OK (ADL Overdrive8): velocidade minima da ventoinha em ", "OK (ADL Overdrive8): minimum fan speed at ",
            "OK (IGCL): ventoinha em ", "OK (IGCL): fan at ",
            "OK (NVML): ventoinha(s) em ", "OK (NVML): fan(s) at ",
            "OK: automatico", "OK: automatic",
            "Observacao", "Note",
            "Ocultar sem leitura", "Hide without reading",
            "Offset do clock da memoria, em MHz", "Memory clock offset, in MHz",
            "Offset do clock da memoria", "Memory clock offset",
            "Offset do clock do nucleo, em MHz acima do padrao", "Core clock offset, in MHz above default",
            "Offset do clock do nucleo", "Core clock offset",
            "Ok - driver de sensores funcionando", "OK - sensor driver working",
            "OpenCL nao encontrado (instale o driver da placa de video).", "OpenCL not found (install the graphics driver).",
            "OpenCL: erro ao compilar (", "OpenCL: compile error (",
            "OpenCL: erro ", "OpenCL: error ",
            "OpenCL: falha ao carregar o programa (", "OpenCL: failed to load the program (",
            "OpenCL: falha ao criar contexto (", "OpenCL: failed to create context (",
            "OpenCL: falha na fila de comandos (", "OpenCL: command queue failure (",
            "OpenCL: sem memoria de video para o teste.", "OpenCL: not enough video memory for the test.",
            "OpenGL indisponivel (", "OpenGL unavailable (",
            "Os sensores da CPU (temperatura, tensao, consumo) precisam do driver do LibreHardwareMonitor, e ele nao esta conseguindo ler esta CPU.\n\n", "CPU sensors (temperature, voltage, power) need the LibreHardwareMonitor driver, and it is unable to read this CPU.\n\n",
            "Outra", "Other",
            "Outros", "Others",
            "Outro", "Other",
            "Overclock / limites - ", "Overclock / limits - ",
            "Overclock / ventoinha", "Overclock / fan",
            "Overclock indisponivel para esta GPU. ", "Overclock unavailable for this GPU. ",
            "Overdrive indisponivel nesta placa/driver (ative \"Ajuste manual\" no AMD Adrenalin)", "Overdrive unavailable on this card/driver (enable \"Manual tuning\" in AMD Adrenalin)",
            "Overdrive6_FanSpeed_Reset erro ", "Overdrive6_FanSpeed_Reset error ",
            "Overdrive6_FanSpeed_Set erro ", "Overdrive6_FanSpeed_Set error ",
            "Overdrive8: nucleo = clock maximo; memoria = clock maximo (MHz reais); potencia em % relativo ao padrao.", "Overdrive8: core = max clock; memory = max clock (real MHz); power in % relative to default.",
            "Overdrive8_Init_Setting_Get erro ", "Overdrive8_Init_Setting_Get error ",
            "Overdrive8_Setting_Set erro ", "Overdrive8_Setting_Set error ",
            "Overlay do DLSS ativado.", "DLSS overlay enabled.",
            "Overlay do DLSS desativado.", "DLSS overlay disabled.",
            "PAUSADO", "PAUSED",
            "PCIe (agora)", "PCIe (now)",
            "PCIe (maximo da placa)", "PCIe (card maximum)",
            "PCIe - geracao", "PCIe - generation",
            "PCIe - largura (x)", "PCIe - width (x)",
            "Padrao do Windows (nao alterado)", "Windows default (unchanged)",
            "Padrao", "Default",
            "Parado por seguranca: temperatura de ", "Stopped for safety: temperature of ",
            "Passe o mouse sobre o grafico para ver os valores de cada instante", "Hover over the graph to see the values at each moment",
            "Pastas e arquivos irao para a Lixeira.\n", "Folders and files will go to the Recycle Bin.\n",
            "Pastas e arquivos serao APAGADOS de vez.\n", "Folders and files will be DELETED permanently.\n",
            "Pausar", "Pause",
            "PawnIO instalado! Feche e abra o BunoTunner para os sensores, ventoinhas e timings passarem a usar o driver.", "PawnIO installed! Close and reopen BunoTunner so sensors, fans and timings start using the driver.",
            "PawnIO instalado: ", "PawnIO installed: ",
            "PawnIO instalado", "PawnIO installed",
            "PawnIO nao instalado; ", "PawnIO not installed; ",
            "PawnIO: driver usado pelo LibreHardwareMonitor para ler sensores da CPU/placa-mae e controlar as ventoinhas da placa-mae. Clique para baixar e instalar.", "PawnIO: driver used by LibreHardwareMonitor to read CPU/motherboard sensors and control motherboard fans. Click to download and install.",
            "PawnIOLib.dll nao encontrada; ", "PawnIOLib.dll not found; ",
            "PawnIOLib.dll sem as funcoes esperadas; ", "PawnIOLib.dll without the expected functions; ",
            "Pico:", "Peak:",
            "Placa-mae (Super I/O)", "Motherboard (Super I/O)",
            "Placa-mae", "Motherboard",
            "Ponto de restauracao criado.", "Restore point created.",
            "Por que?\n", "Why?\n",
            "Potencia", "Power",
            "Pouco espaco livre no drive ", "Little free space on drive ",
            "Processador - Zona termica (Windows/ACPI)", "Processor - Thermal zone (Windows/ACPI)",
            "Processador de video", "Video processor",
            "Processador", "Processor",
            "Procurando restos de ", "Looking for leftovers of ",
            "Procurando restos...", "Looking for leftovers...",
            "Proibido", "Forbidden",
            "Pronto: ", "Ready: ",
            "Pronto", "Ready",
            "Provedor (ISP/ASN): ", "Provider (ISP/ASN): ",
            "Quando o processo abrir, o preset e aplicado sozinho (ex.: jogo.exe -> nucleos fisicos).", "When the process starts, the preset is applied automatically (e.g. game.exe -> physical cores).",
            "Quantidade de ventoinhas", "Number of fans",
            "RAM - mapa de memoria", "RAM - memory map",
            "RAM em uso", "RAM in use",
            "REMOCAO FORCADA de \"", "FORCED REMOVAL of \"",
            "RPM = giros por minuto. % = potencia enviada. \"so leitura\" = a placa-mae nao permite controle por software.", "RPM = revolutions per minute. % = power sent. \"read-only\" = the motherboard does not allow software control.",
            "Recomendamos a instalacao do PawnIO.\n\n", "We recommend installing PawnIO.\n\n",
            "Redefinir: ", "Reset: ",
            "Registro", "Registry",
            "Regra aplicada: ", "Rule applied: ",
            "Removendo...", "Removing...",
            "Remover alerta", "Remove alert",
            "Remover ", "Remove ",
            "Renomear ventoinha", "Rename fan",
            "Renomear...", "Rename...",
            "Resolucao atual", "Current resolution",
            "Resultados copiados.", "Results copied.",
            "Revisao", "Revision",
            "Ring0 (LHM) com PCI: ", "Ring0 (LHM) with PCI: ",
            "Roda CPU, FPU, RAM e GPU um apos o outro, cada um com a duracao do seu bloco (o SSD fica de fora para nao gerar desgaste).", "Runs CPU, FPU, RAM and GPU one after another, each with its block's duration (the SSD is left out to avoid wear).",
            "Rodando...", "Running...",
            "Ruim", "Poor",
            "SEGURANCA: ", "SECURITY: ",
            "SEGURANCA", "SECURITY",
            "SLAT (paginacao aninhada)", "SLAT (nested paging)",
            "SSD - mapa de blocos do disco", "SSD - disk block map",
            "Salvar imagem do render (PNG)", "Save render image (PNG)",
            "Salvo em ", "Saved to ",
            "Salvo: ", "Saved: ",
            "Se esquentar demais, as ventoinhas vao a 100% mesmo no modo manual.", "If it gets too hot, the fans go to 100% even in manual mode.",
            "Segundos", "Seconds",
            "Seguranca baseada em virtualizacao (VBS)", "Virtualization-based security (VBS)",
            "Seguranca: forcar 100% se a temperatura passar de", "Safety: force 100% if the temperature goes above",
            "Selecionadas a 100% por 5 s. Veja/ouca qual acelerou e acompanhe o RPM na lista.", "Selected ones at 100% for 5 s. See/hear which one sped up and follow the RPM in the list.",
            "Selecionado", "Selected",
            "Selecionar so as controlaveis", "Select only the controllable ones",
            "Selecionar todas", "Select all",
            "Selecionar: CPU", "Select: CPU",
            "Selecionar: GPU", "Select: GPU",
            "Selecionar: bombas / AIO", "Select: pumps / AIO",
            "Selecionar: gabinete", "Select: case",
            "Selecione na lista pelo menos uma ventoinha controlavel.", "Select at least one controllable fan in the list.",
            "Selecione pelo menos uma ventoinha controlavel.", "Select at least one controllable fan.",
            "Selecione uma ventoinha para renomear.", "Select a fan to rename.",
            "Sem copia do registro.", "No registry copy.",
            "Sem dados", "No data",
            "Sem ele ficam sem leitura: varios sensores de temperatura da CPU e da placa-mae, o controle das ventoinhas ligadas a placa-mae e os timings da memoria RAM.\n\n", "Without it these have no reading: several CPU and motherboard temperature sensors, control of the fans connected to the motherboard, and the RAM timings.\n\n",
            "Sem leitura de temperatura - veja o LEIA-ME (driver bloqueado?)", "No temperature reading - see the README (driver blocked?)",
            "Sensores atualizando a cada ", "Sensors updating every ",
            "Sensores da CPU sem leitura", "CPU sensors without reading",
            "Sensores expostos (LHM)", "Exposed sensors (LHM)",
            "Servico", "Service",
            "Servidor DHCP: ", "DHCP server: ",
            "So no grafico", "Only on graph",
            "Soquete", "Socket",
            "Sufixo DNS: ", "DNS suffix: ",
            "Tarefa", "Task",
            "Taxa de atualizacao do monitor", "Monitor refresh rate",
            "Taxa de dados", "Data rate",
            "Taxa (", "Rate (",
            "Temp Limit (alvo termico)", "Temp Limit (thermal target)",
            "Temp. alvo (limite de temperatura)", "Target temp. (temperature limit)",
            "Temp. de desligamento", "Shutdown temp.",
            "Temp. de slowdown (throttle)", "Slowdown temp. (throttle)",
            "Temp. maxima da GPU", "Max GPU temp.",
            "Temp. maxima da memoria", "Max memory temp.",
            "Temperatura do nucleo", "Core temperature",
            "Temperatura maxima registrada", "Max recorded temperature",
            "Temperatura hotspot", "Hotspot temperature",
            "Temperatura: ", "Temperature: ",
            "Temperatura", "Temperature",
            "Tempo entre as leituras dos sensores (100 a 10000 ms). Menos = mais fluido, mas usa mais CPU.", "Time between sensor readings (100 to 10000 ms). Lower = smoother, but uses more CPU.",
            "Tempo:", "Time:",
            "Teste cancelado.", "Test cancelled.",
            "Teste concluido.", "Test finished.",
            "Teste finalizado.", "Test ended.",
            "Threads (processadores logicos)", "Threads (logical processors)",
            "Tipo (LHM)", "Type (LHM)",
            "Tipo / interface", "Type / interface",
            "Tipo: ", "Type: ",
            "Tipo:", "Type:",
            "Todas as ventoinhas voltaram ao automatico.", "All fans went back to automatic.",
            "Todos os tipos", "All types",
            "Todos (", "All (",
            "Todas", "All",
            "Tudo certo. Em HDDs, observe erros e temperatura (ideal abaixo de 50 C).", "All good. On HDDs, watch errors and temperature (ideally below 50 C).",
            "Tudo certo. SSDs perdem vida conforme a quantidade de dados gravados; valores abaixo de 30% pedem atencao.", "All good. SSDs lose life according to the amount of data written; values below 30% need attention.",
            "Uma copia das chaves do registro sera salva em BunoTunner_backup.", "A copy of the registry keys will be saved in BunoTunner_backup.",
            "Usando ", "Using ",
            "Use isto para programas que nao aparecem na lista (portateis, restos de algo ja removido, etc.).\n\nO que voce quer escolher?", "Use this for programs that do not appear in the list (portable ones, leftovers of something already removed, etc.).\n\nWhat do you want to choose?",
            "Uso (%)", "Usage (%)",
            "Uso do controlador de memoria", "Memory controller usage",
            "Uso do nucleo", "Core usage",
            "Uso total", "Total usage",
            "Usuario: ", "User: ",
            "VRAM em uso (%)", "VRAM in use (%)",
            "VRAM usada", "VRAM used",
            "Valor invalido.", "Invalid value.",
            "Valores atuais (CSV)...", "Current values (CSV)...",
            "Valores originais restaurados.", "Original values restored.",
            "Variacao", "Variation",
            "Velocidade:", "Speed:",
            "Ventoinha (%)", "Fan (%)",
            "Ventoinha (RPM)", "Fan (RPM)",
            "Ventoinha da CPU", "CPU fan",
            "Ventoinhas da GPU", "GPU fans",
            "Ventoinhas", "Fans",
            "Versao do driver (Windows)", "Driver version (Windows)",
            "Virtualizacao no firmware (VT-x / AMD-V)", "Virtualization in firmware (VT-x / AMD-V)",
            "Voltagem:", "Voltage:",
            "Voltagem", "Voltage",
            "WMI indisponivel: ", "WMI unavailable: ",
            "Zerar min/max deste sensor", "Reset min/max of this sensor",
            "Zona termica", "Thermal zone",
            "Zona ", "Zone ",
            "\" (pasta ", "\" (folder ",
            "\" (renomeie as suas para classificar).", "\" (rename yours to classify).",
            "\" passar de (", "\" goes above (",
            "\" salvo.", "\" saved.",
            "\" selecionada(s).", "\" selected.",
            "\"?\n\n1) O desinstalador oficial sera aberto (siga as telas dele).\n2) Depois o BunoTunner procura o que sobrou para voce revisar.", "\"?\n\n1) The official uninstaller will open (follow its screens).\n2) Then BunoTunner looks for what was left behind for you to review.",
            "\"\n\nIsto NAO usa o desinstalador: encerra os processos do programa, para os servicos dele e lista tudo o que for dele (pasta, registro, inicializacao, tarefas) para voce apagar.\n\nNada e apagado antes de voce revisar e confirmar a lista. Continuar?", "\"\n\nThis does NOT use the uninstaller: it ends the program's processes, stops its services and lists everything that belongs to it (folder, registry, startup, tasks) for you to delete.\n\nNothing is deleted before you review and confirm the list. Continue?",
            "\n(copiado para a area de transferencia)", "\n(copied to the clipboard)",
            "\n(tempo esgotado)", "\n(timed out)",
            "\n\nSe o arquivo esta em uso, feche o programa (ou use 'Remocao forcada') e tente de novo; ou reinicie o PC.", "\n\nIf the file is in use, close the program (or use 'Forced removal') and try again; or restart the PC.",
            "a LibreHardwareMonitorLib.dll da pasta libs nao traz o modulo PawnIO (DLL antiga: atualize); ", "the LibreHardwareMonitorLib.dll in the libs folder does not include the PawnIO module (old DLL: update it); ",
            "acesso negado (rode como administrador)", "access denied (run as administrator)",
            "adaptador AMD nao encontrado no ADL", "AMD adapter not found in ADL",
            "alta", "high",
            "media", "medium",
            "argumento invalido", "invalid argument",
            "arquivo protegido", "protected file",
            "atiadlxx.dll nao encontrada (instale o driver Radeon/Adrenalin)", "atiadlxx.dll not found (install the Radeon/Adrenalin driver)",
            "chave invalida", "invalid key",
            "chave protegida", "protected key",
            "clock de aplicativo", "application clock",
            "codigo ", "code ",
            "controlador de memória da CPU (AMD Ryzen, canal ", "CPU memory controller (AMD Ryzen, channel ",
            "desligado no .ini (vendoroc=0)", "disabled in the .ini (vendoroc=0)",
            "distancia ate o TjMax", "distance to TjMax",
            "em uso e nao foi possivel agendar a exclusao", "in use and the deletion could not be scheduled",
            "entrada em Programas e Recursos", "entry in Programs and Features",
            "erro ao inspecionar a DLL: ", "error inspecting the DLL: ",
            "erro generico", "generic error",
            "erro - ", "error - ",
            "erro ", "error ",
            "esta placa usa Overdrive ", "this card uses Overdrive ",
            "esta plataforma (", "this platform (",
            "este ajuste nao e exposto pelo driver Intel desta GPU", "this setting is not exposed by this GPU's Intel driver",
            "fabricante sem suporte de OC nesta versao", "manufacturer without OC support in this version",
            "falha ao carregar PawnIOLib.dll; ", "failed to load PawnIOLib.dll; ",
            "falhou", "failed",
            "fase 2 concluida (+", "phase 2 finished (+",
            "fase 2 erro: ", "phase 2 error: ",
            "fase 2: ", "phase 2: ",
            "feature desligada no Adrenalin", "feature turned off in Adrenalin",
            "formato de pixel OpenGL indisponivel", "OpenGL pixel format unavailable",
            "freio de energia (HW)", "power brake (HW)",
            "frontal", "front",
            "gabinete", "case",
            "grafico", "graph",
            "indisponivel", "unavailable",
            "janela mostrada", "window shown",
            "leitura direta do controlador: só CPUs AMD Ryzen nesta versão", "direct controller reading: only AMD Ryzen CPUs in this version",
            "limite de potencia (SW)", "power limit (SW)",
            "limite de potencia fixo nesta placa (normal em notebooks: o fabricante trava o TDP)", "fixed power limit on this card (normal on laptops: the manufacturer locks the TDP)",
            "limite de temperatura nao ajustavel nesta placa", "temperature limit not adjustable on this card",
            "limite termico (HW)", "thermal limit (HW)",
            "limite termico (SW)", "thermal limit (SW)",
            "nao foi possivel criar o contexto OpenGL", "could not create the OpenGL context",
            "nao foi possivel ler o controlador de memoria: ", "could not read the memory controller: ",
            "nao suportado nesta GPU/notebook", "not supported on this GPU/laptop",
            "nao suportado nesta placa", "not supported on this card",
            "nao suportado", "not supported",
            "nenhum adaptador ADL", "no ADL adapter",
            "nenhum adaptador AMD no ADL (", "no AMD adapter in ADL (",
            "nenhum dispositivo Intel (ctlEnumerateDevices 0x", "no Intel device (ctlEnumerateDevices 0x",
            "nvml.dll nao encontrada", "nvml.dll not found",
            "não informado pela BIOS", "not reported by the BIOS",
            "o Windows nao informou discos", "Windows did not report disks",
            "o driver Intel nao liberou overclock para esta GPU (so Arc dedicadas; graficos integrados nao tem)", "the Intel driver did not enable overclocking for this GPU (dedicated Arc only; integrated graphics do not have it)",
            "o driver nao informa a faixa (comum em notebooks); valores acima do limite serao recusados", "the driver does not report the range (common on laptops); values above the limit will be rejected",
            "o driver nao liberou este ajuste (", "the driver did not enable this setting (",
            "ociosa (normal)", "idle (normal)",
            "pawnio_load falhou (0x", "pawnio_load failed (0x",
            "pawnio_open falhou (0x", "pawnio_open failed (0x",
            "recurso inexistente neste driver", "feature not present in this driver",
            "registradores do controlador nao reconhecidos nesta CPU (use o botao Diagnostico)", "controller registers not recognized on this CPU (use the Diagnostics button)",
            "relatório do CPU-Z (", "CPU-Z report (",
            "rode como Administrador", "run as Administrator",
            "sem backend para este fabricante", "no backend for this manufacturer",
            "sem driver NVIDIA", "no NVIDIA driver",
            "sem implementacao", "not implemented",
            "sem permissao no registro: ", "no registry permission: ",
            "sem reset", "no reset",
            "sensores prontos (fase 1)", "sensors ready (phase 1)",
            "slowdown de hardware", "hardware slowdown",
            "tipo desconhecido", "unknown type",
            "traseira", "rear",
            "versao de estrutura incompativel", "incompatible structure version",
            "papel de parede", "wallpaper",
            "sim", "yes",
            "nao", "no",
            "Sim", "Yes",
            "Grupo;Sensor;Tipo;Valor;Minimo;Maximo;Unidade", "Group;Sensor;Type;Value;Minimum;Maximum;Unit",
            "IGCL incompleta", "IGCL incomplete",
            "IGCL sem Overclock", "IGCL without Overclock",
            "Sistema operacional", "Operating system",
            "Driver (registro)", "Driver (registry)",
            "pasta de instalacao", "installation folder",
            "pasta protegida", "protected folder",
            "Temp max", "Max temp",
            "NVML iniciado", "NVML started",
            "Rede", "Network",
            "Placa de video", "Graphics card",
            " MB em ", " MB in ",
            "LHM configurado", "LHM configured",
            "MainForm: inicio", "MainForm: start",
            "MainForm: paginas criadas", "MainForm: pages created",
            "SensorHub.Open concluido", "SensorHub.Open done",
            "tema e PawnIO verificados", "theme and PawnIO checked",
            "ns x tamanho do bloco", "ns x block size" };

        static Lang()
        {
            for (int i = 0; i + 1 < Pairs.Length; i += 2) en[Pairs[i]] = Pairs[i + 1];
            for (int i = 0; i + 1 < Pairs2.Length; i += 2) en[Pairs2[i]] = Pairs2[i + 1];
            Dictionary<char, List<string>> tmp = new Dictionary<char, List<string>>();
            foreach (string k in en.Keys)
            {
                if (k.Length == 0 || k == en[k]) continue;
                List<string> l;
                if (!tmp.TryGetValue(k[0], out l)) { l = new List<string>(); tmp[k[0]] = l; }
                l.Add(k);
            }
            foreach (KeyValuePair<char, List<string>> kv in tmp)
            {
                kv.Value.Sort(delegate(string a, string b) { return b.Length != a.Length ? b.Length.CompareTo(a.Length) : string.CompareOrdinal(a, b); });
                byFirst[kv.Key] = kv.Value.ToArray();
            }
        }

        public static void Init()
        {
            string v = Settings.Get("lang", "auto");
            if (v == "pt") Cur = 0;
            else if (v == "en") Cur = 1;
            else Cur = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower() == "pt" ? 0 : 1;
            lock (cache) cache.Clear();
        }

        static bool IsW(char c) { return char.IsLetterOrDigit(c); }

        // traduz um texto qualquer (inteiro ou so trechos conhecidos dele); em portugues devolve o proprio texto
        public static string Pick(string pt, string en_) { return Cur == 1 ? en_ : pt; }

        public static string T(string pt)
        {
            if (Cur == 0 || string.IsNullOrEmpty(pt)) return pt;
            string r;
            if (en.TryGetValue(pt, out r)) return r;
            lock (cache) { if (cache.TryGetValue(pt, out r)) return r; }
            r = Frag(pt);
            lock (cache) { if (cache.Count > 6000) cache.Clear(); cache[pt] = r; }
            return r;
        }

        static string Frag(string s)
        {
            StringBuilder sb = null;
            int last = 0, i = 0, n = s.Length;
            while (i < n)
            {
                string[] ks;
                if (byFirst.TryGetValue(s[i], out ks) && !(i > 0 && IsW(s[i - 1]) && IsW(s[i])))
                {
                    string hit = null;
                    for (int j = 0; j < ks.Length; j++)
                    {
                        string k = ks[j];
                        if (i + k.Length > n) continue;
                        if (string.CompareOrdinal(s, i, k, 0, k.Length) != 0) continue;
                        int e = i + k.Length;
                        if (IsW(k[k.Length - 1]) && e < n && IsW(s[e])) continue;
                        hit = k; break;
                    }
                    if (hit != null)
                    {
                        if (sb == null) sb = new StringBuilder(n + 24);
                        sb.Append(s, last, i - last).Append(en[hit]);
                        i += hit.Length; last = i;
                        continue;
                    }
                }
                i++;
            }
            if (sb == null) return s;
            sb.Append(s, last, n - last);
            return sb.ToString();
        }

        static Holder H(object k) { return hold.GetValue(k, delegate(object o) { return new Holder(); }); }

        // guarda o texto original (portugues) e mostra a versao no idioma atual
        static void Tx(object key, string cur, Action<string> set)
        {
            if (string.IsNullOrEmpty(cur)) return;
            Holder h;
            if (!hold.TryGetValue(key, out h))
            {
                string t0 = T(cur);
                if (t0 == cur) return;
                h = H(key); h.Src = cur;
                Put(set, t0);
                return;
            }
            if (h.Src == null || (cur != h.Src && cur != T(h.Src))) h.Src = cur;
            string t = T(h.Src);
            if (t != cur) Put(set, t);
        }

        static void Put(Action<string> set, string t)
        {
            bool was = busy;
            busy = true;
            try { set(t); } finally { busy = was; }
        }

        // acompanha mudancas de texto feitas pelo programa depois (mensagens de status, resultados...)
        static void Hook(Control c)
        {
            Holder h = H(c);
            if (h.Hooked) return;
            h.Hooked = true;
            c.TextChanged += delegate
            {
                if (busy) return;
                string cur = c.Text;
                if (string.IsNullOrEmpty(cur)) return;
                H(c).Src = cur;
                if (Cur == 0) return;
                string t = T(cur);
                if (t != cur) { Put(delegate(string x) { c.Text = x; }, t); if (c is Form) c.Invalidate(); }
            };
        }

        static void ApplyOne(Control c)
        {
            Control cc = c;
            if (c is Label || c is XpButton || c is XpOption || c is Form)
            {
                if (!string.IsNullOrEmpty(c.Text)) Tx(c, c.Text, delegate(string t) { cc.Text = t; if (cc is Form) cc.Invalidate(); });
                Hook(c);
            }
            XpGroup g = c as XpGroup;
            if (g != null && !string.IsNullOrEmpty(g.Title)) Tx(g, g.Title, delegate(string t) { g.Title = t; });
            TextBox tb = c as TextBox;
            if (tb != null && tb.ReadOnly) { if (!string.IsNullOrEmpty(tb.Text)) Tx(tb, tb.Text, delegate(string t) { tb.Text = t; }); Hook(tb); }
            ListView lv = c as ListView;
            if (lv != null) foreach (ColumnHeader h in lv.Columns) { ColumnHeader hh = h; if (!string.IsNullOrEmpty(h.Text)) Tx(h, h.Text, delegate(string t) { hh.Text = t; }); }
            ComboBox cb = c as ComboBox;
            if (cb != null && cb.DropDownStyle == ComboBoxStyle.DropDownList && !(cb.Tag is string && (string)cb.Tag == "nolang") && cb.DrawMode == DrawMode.Normal)
            {
                cb.DrawMode = DrawMode.OwnerDrawFixed;
                cb.ItemHeight = cb.Font.Height + 2;
                cb.DrawItem += DrawCombo;
            }
            if (c.ContextMenuStrip != null) Menu(c.ContextMenuStrip);
        }

        static void DrawCombo(object sender, DrawItemEventArgs e)
        {
            ComboBox cb = (ComboBox)sender;
            e.DrawBackground();
            if (e.Index >= 0 && e.Index < cb.Items.Count)
            {
                object it = cb.Items[e.Index];
                string s = it == null ? "" : T(it.ToString());
                Color fc = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0 ? SystemColors.HighlightText : cb.ForeColor;
                TextRenderer.DrawText(e.Graphics, s, e.Font, new Rectangle(e.Bounds.X + 1, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height), fc,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            e.DrawFocusRectangle();
        }

        // menus de contexto (itens traduzidos; fica registrado para trocar de idioma ao vivo)
        public static void Menu(ToolStrip m)
        {
            if (m == null) return;
            Holder h = H(m);
            if (!h.Hooked) { h.Hooked = true; menus.Add(new WeakReference(m)); }
            MenuItems(m.Items);
        }

        static void MenuItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem it in items)
            {
                ToolStripItem ii = it;
                if (!string.IsNullOrEmpty(it.Text)) Tx(it, it.Text, delegate(string t) { ii.Text = t; });
                ToolStripMenuItem mi = it as ToolStripMenuItem;
                if (mi != null && mi.HasDropDownItems) MenuItems(mi.DropDownItems);
            }
        }

        // traduz a janela inteira (e as que forem adicionadas depois)
        public static void Apply(Control c, ToolTip tip)
        {
            if (c == null) return;
            ApplyOne(c);
            if (hookedAdd.Add(c))
            {
                Control cc = c;
                c.Disposed += delegate { hookedAdd.Remove(cc); };
                c.ControlAdded += delegate(object sd, ControlEventArgs e) { Apply(e.Control, null); };
            }
            foreach (Control k in c.Controls) Apply(k, null);
        }
        static HashSet<Control> hookedAdd = new HashSet<Control>();

        // listas (ListView): troca o texto das celulas; roda de tempos em tempos e ao trocar de idioma
        static void SweepLv(ListView lv)
        {
            foreach (ColumnHeader h in lv.Columns) { ColumnHeader hh = h; if (!string.IsNullOrEmpty(h.Text)) Tx(h, h.Text, delegate(string t) { hh.Text = t; }); }
            foreach (ListViewItem it in lv.Items)
                foreach (ListViewItem.ListViewSubItem sub in it.SubItems)
                {
                    ListViewItem.ListViewSubItem ss = sub;
                    if (!string.IsNullOrEmpty(sub.Text)) Tx(sub, sub.Text, delegate(string t) { ss.Text = t; });
                }
        }

        static void Sweep(Control c, bool all)
        {
            ListView lv = c as ListView;
            if (lv != null && (all || lv.Visible)) SweepLv(lv);
            ComboBox cb = c as ComboBox;
            if (cb != null && all) cb.Invalidate();
            foreach (Control k in c.Controls) Sweep(k, all);
        }

        // troca de idioma ao vivo
        public static void Reapply()
        {
            lock (cache) cache.Clear();
            List<Form> fs = new List<Form>();
            foreach (Form f0 in Application.OpenForms) fs.Add(f0);
            foreach (Form f in fs) { Apply(f, null); Sweep(f, true); }
            foreach (LTip t in Tips) t.Retranslate();
            for (int i = menus.Count - 1; i >= 0; i--)
            {
                ToolStrip m = menus[i].Target as ToolStrip;
                if (m == null) menus.RemoveAt(i); else MenuItems(m.Items);
            }
            if (Changed != null) Changed();
        }

        // a cada 0,5 s procura textos novos em listas (so quando esta em ingles)
        public static void StartSweeper()
        {
            if (sweeper != null) return;
            sweeper = new Timer();
            sweeper.Interval = 250;
            sweeper.Tick += delegate
            {
                if (Cur == 0) return;
                List<Form> fs = new List<Form>();
                foreach (Form f0 in Application.OpenForms) fs.Add(f0);
                foreach (Form f in fs)
                {
                    if (!hookedAdd.Contains(f)) Apply(f, null);   // janela simples criada sem passar pela traducao
                    Sweep(f, false);
                }
            };
            sweeper.Start();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            Start();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Start()
        {
            try
            {
                Application.SetCompatibleTextRenderingDefault(false);
                Lang.Init();
                Xp.SetTheme(Xp.IdOf(Settings.Get("theme", "luna")));
                Application.EnableVisualStyles();
                try { PawnIo.CheckAtStartup(null); } catch (Exception) { }
                Startup.Mark("tema e PawnIO verificados");
                Startup.ShowSplash();
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                Dlg.Show(ex.ToString(), "BunoTunner - erro fatal");
            }
        }

        static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string n = new AssemblyName(args.Name).Name + ".dll";
            using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream(n))
            {
                if (st == null) return null;
                MemoryStream ms = new MemoryStream();
                byte[] buf = new byte[81920];
                int r;
                while ((r = st.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, r);
                return Assembly.Load(ms.ToArray());
            }
        }
    }

    // Paleta completa de um tema (os campos tem os mesmos nomes dos campos estaticos de Xp)
    class Pal
    {
        public Color Face = Color.FromArgb(236, 233, 216), Border = Color.FromArgb(0, 60, 116), GroupLine = Color.FromArgb(208, 208, 191), GroupText = Color.FromArgb(0, 70, 213);
        public Color Sel = Color.FromArgb(49, 106, 197), TextBorder = Color.FromArgb(127, 157, 185);
        public Color PaneTop = Color.FromArgb(123, 162, 231), PaneBot = Color.FromArgb(99, 117, 214), PaneMid = Color.FromArgb(110, 140, 222), PaneText = Color.White;
        public Color GraphBg = Color.Black, GraphGrid = Color.FromArgb(0, 90, 0), GraphText = Color.Lime;
        public Color OptBorder = Color.FromArgb(29, 82, 129), OptMark = Color.FromArgb(33, 161, 33);
        public Color Text = Color.Black, Dim = Color.Gray, Good = Color.FromArgb(0, 100, 0), Bad = Color.Firebrick, Dis = Color.FromArgb(172, 168, 153);
        public Color EditBg = Color.White, HitBg = Color.FromArgb(255, 205, 205);
        public Color BtnA = Color.White, BtnB = Color.FromArgb(214, 208, 197), BtnDownA = Color.FromArgb(205, 201, 190), BtnDownB = Color.FromArgb(226, 223, 214), Hot = Color.FromArgb(255, 196, 60);
        public Color SelA = Color.FromArgb(228, 240, 255), SelB = Color.FromArgb(150, 186, 246);
        public Color OptA = Color.FromArgb(215, 215, 208), OptB = Color.White;
        public Color TrackBg = Color.White, Tick = Color.FromArgb(172, 168, 153), ThumbA = Color.White, ThumbB = Color.FromArgb(214, 208, 197), ThumbLine = Color.FromArgb(106, 141, 185);
        public Color CapA = Color.FromArgb(9, 151, 255), CapB = Color.FromArgb(0, 80, 238), CapFrame = Color.FromArgb(0, 60, 220), CapText = Color.White, CapShadow = Color.FromArgb(10, 24, 131);
        public Color CapBtn1 = Color.FromArgb(100, 160, 255), CapBtn2 = Color.FromArgb(35, 90, 225), CapClose1 = Color.FromArgb(226, 130, 105), CapClose2 = Color.FromArgb(195, 60, 30), CapX = Color.White;
        public Color ProgBg = Color.White, ProgBorder = Color.FromArgb(104, 104, 104), Prog1 = Color.FromArgb(150, 240, 150), Prog2 = Color.FromArgb(36, 180, 48);
        public Color SepDark = Color.FromArgb(70, 90, 190), SepLight = Color.FromArgb(170, 195, 245);
        public Color Hi = Color.White;
        public int Rad = 3;
        public bool Bevel, Square, Dark;
        public Font Ui = new Font("Tahoma", 8.25f), UiBold = new Font("Tahoma", 8.25f, FontStyle.Bold);
        public Font Caption = new Font("Trebuchet MS", 11f, FontStyle.Bold), Big = new Font("Tahoma", 15f, FontStyle.Bold);
        public Font Title = new Font("Trebuchet MS", 14f, FontStyle.Bold | FontStyle.Italic);

        static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public static Pal Make(int t)
        {
            Pal p = new Pal();
            switch (t)
            {
                case 1: // Frutiger Aero (Windows 7)
                    p.Face = C(246, 249, 252); p.Border = C(112, 112, 112); p.GroupLine = C(200, 216, 232); p.GroupText = C(30, 57, 91);
                    p.Sel = C(51, 153, 255); p.TextBorder = C(171, 173, 179);
                    p.PaneTop = C(200, 228, 250); p.PaneBot = C(112, 178, 224); p.PaneMid = C(150, 200, 238); p.PaneText = C(24, 56, 100);
                    p.GraphBg = C(8, 22, 40); p.GraphGrid = C(28, 70, 110); p.GraphText = C(140, 220, 255);
                    p.OptBorder = C(142, 143, 143); p.OptMark = C(30, 57, 91);
                    p.Dis = C(109, 109, 109); p.Tick = C(172, 168, 153); p.ThumbB = C(206, 216, 232); p.ThumbLine = C(104, 122, 152);
                    p.OptA = C(214, 222, 232); p.SepDark = C(110, 140, 195); p.SepLight = C(220, 240, 255);
                    break;
                case 2: // Dark Mode / AMOLED
                    p.Face = C(14, 14, 16); p.Border = C(90, 90, 100); p.GroupLine = C(60, 60, 66); p.GroupText = C(90, 170, 255);
                    p.Sel = C(38, 110, 200); p.TextBorder = C(70, 70, 78);
                    p.PaneTop = C(24, 24, 30); p.PaneBot = C(6, 6, 10); p.PaneMid = C(15, 15, 20); p.PaneText = C(150, 150, 160);
                    p.GraphBg = C(0, 0, 0); p.GraphGrid = C(40, 40, 46); p.GraphText = C(90, 220, 140);
                    p.OptBorder = C(120, 120, 130); p.OptMark = C(90, 170, 255);
                    p.Text = C(225, 225, 230); p.Dim = C(140, 140, 150); p.Good = C(80, 200, 120); p.Bad = C(255, 95, 95); p.Dis = C(100, 100, 108);
                    p.EditBg = C(24, 24, 28); p.HitBg = C(90, 30, 30);
                    p.BtnA = C(50, 50, 56); p.BtnB = C(28, 28, 32); p.BtnDownA = C(20, 20, 24); p.BtnDownB = C(36, 36, 42); p.Hot = C(90, 170, 255);
                    p.SelA = C(30, 70, 120); p.SelB = C(20, 50, 95); p.OptA = C(50, 50, 56); p.OptB = C(30, 30, 34);
                    p.TrackBg = C(30, 30, 34); p.Tick = C(100, 100, 110); p.ThumbA = C(100, 100, 110); p.ThumbB = C(60, 60, 68); p.ThumbLine = C(150, 150, 165);
                    p.CapA = C(34, 34, 40); p.CapB = C(0, 0, 0); p.CapFrame = C(0, 0, 0); p.CapText = C(235, 235, 240); p.CapShadow = C(0, 0, 0);
                    p.CapBtn1 = C(80, 80, 90); p.CapBtn2 = C(44, 44, 50); p.CapClose1 = C(210, 80, 70); p.CapClose2 = C(150, 30, 30);
                    p.ProgBg = C(30, 30, 34); p.ProgBorder = C(90, 90, 100); p.Prog1 = C(110, 200, 255); p.Prog2 = C(30, 120, 220);
                    p.SepDark = C(0, 0, 0); p.SepLight = C(60, 60, 70);
                    p.Hi = C(190, 196, 210); p.CapX = C(214, 218, 228); p.PaneText = C(170, 176, 190);
                    p.Rad = 4; p.Dark = true;
                    break;
                case 3: // Neon / Cyberpunk
                    p.Face = C(13, 8, 28); p.Border = C(0, 240, 255); p.GroupLine = C(130, 30, 170); p.GroupText = C(255, 60, 220);
                    p.Sel = C(180, 0, 200); p.TextBorder = C(0, 200, 220);
                    p.PaneTop = C(48, 0, 84); p.PaneBot = C(6, 0, 26); p.PaneMid = C(24, 0, 50); p.PaneText = C(0, 240, 255);
                    p.GraphBg = C(4, 0, 12); p.GraphGrid = C(60, 0, 90); p.GraphText = C(0, 255, 200);
                    p.OptBorder = C(0, 240, 255); p.OptMark = C(255, 60, 220);
                    p.Text = C(200, 250, 255); p.Dim = C(150, 130, 200); p.Good = C(60, 255, 150); p.Bad = C(255, 70, 100); p.Dis = C(90, 80, 120);
                    p.EditBg = C(22, 12, 44); p.HitBg = C(100, 0, 60);
                    p.BtnA = C(56, 22, 100); p.BtnB = C(24, 8, 50); p.BtnDownA = C(20, 6, 40); p.BtnDownB = C(64, 22, 108); p.Hot = C(255, 230, 0);
                    p.SelA = C(120, 20, 160); p.SelB = C(70, 0, 110); p.OptA = C(56, 22, 100); p.OptB = C(24, 8, 50);
                    p.TrackBg = C(24, 10, 50); p.Tick = C(150, 60, 200); p.ThumbA = C(255, 90, 230); p.ThumbB = C(140, 20, 170); p.ThumbLine = C(0, 240, 255);
                    p.CapA = C(255, 0, 170); p.CapB = C(60, 0, 140); p.CapFrame = C(0, 230, 255); p.CapText = Color.White; p.CapShadow = C(120, 0, 100);
                    p.CapBtn1 = C(0, 220, 255); p.CapBtn2 = C(0, 110, 200); p.CapClose1 = C(255, 80, 130); p.CapClose2 = C(200, 0, 70);
                    p.ProgBg = C(20, 8, 40); p.ProgBorder = C(0, 240, 255); p.Prog1 = C(0, 255, 220); p.Prog2 = C(255, 0, 200);
                    p.SepDark = C(0, 240, 255); p.SepLight = C(255, 60, 220);
                    p.Hi = C(170, 235, 255); p.CapX = C(200, 250, 255);
                    p.Rad = 2; p.Dark = true;
                    break;
                case 4: // Steampunk
                    p.Face = C(222, 203, 163); p.Border = C(74, 44, 16); p.GroupLine = C(150, 108, 56); p.GroupText = C(110, 52, 8);
                    p.Sel = C(160, 96, 32); p.TextBorder = C(110, 76, 36);
                    p.PaneTop = C(150, 100, 48); p.PaneBot = C(74, 46, 20); p.PaneMid = C(110, 72, 32); p.PaneText = C(255, 226, 160);
                    p.GraphBg = C(28, 20, 10); p.GraphGrid = C(80, 60, 24); p.GraphText = C(240, 190, 80);
                    p.OptBorder = C(74, 44, 16); p.OptMark = C(120, 60, 10);
                    p.Text = C(44, 26, 10); p.Dim = C(110, 88, 58); p.Good = C(30, 95, 35); p.Bad = C(150, 25, 15); p.Dis = C(150, 130, 100);
                    p.EditBg = C(248, 238, 212); p.HitBg = C(235, 170, 140);
                    p.BtnA = C(238, 208, 140); p.BtnB = C(170, 122, 60); p.BtnDownA = C(140, 98, 48); p.BtnDownB = C(190, 150, 90); p.Hot = C(255, 214, 90);
                    p.SelA = C(250, 224, 150); p.SelB = C(200, 150, 70); p.OptA = C(230, 205, 150); p.OptB = C(250, 240, 215);
                    p.TrackBg = C(248, 238, 212); p.Tick = C(110, 76, 36); p.ThumbA = C(240, 210, 140); p.ThumbB = C(160, 112, 52); p.ThumbLine = C(74, 44, 16);
                    p.CapA = C(176, 112, 52); p.CapB = C(82, 46, 16); p.CapFrame = C(58, 34, 12); p.CapText = C(255, 236, 190); p.CapShadow = C(40, 20, 4);
                    p.CapBtn1 = C(230, 190, 110); p.CapBtn2 = C(150, 100, 40); p.CapClose1 = C(200, 90, 50); p.CapClose2 = C(120, 36, 14); p.CapX = C(40, 20, 4);
                    p.ProgBg = C(248, 238, 212); p.ProgBorder = C(74, 44, 16); p.Prog1 = C(230, 180, 70); p.Prog2 = C(150, 90, 20);
                    p.SepDark = C(74, 44, 16); p.SepLight = C(230, 200, 140);
                    // mais cobre / bronze
                    p.Face = C(206, 160, 112); p.GroupLine = C(130, 74, 30); p.GroupText = C(92, 36, 4); p.EditBg = C(238, 214, 174); p.TrackBg = C(238, 214, 174); p.ProgBg = C(238, 214, 174);
                    p.PaneTop = C(205, 118, 58); p.PaneMid = C(150, 82, 36); p.PaneBot = C(86, 40, 16); p.PaneText = C(255, 230, 170);
                    p.BtnA = C(232, 160, 98); p.BtnB = C(166, 90, 40); p.BtnDownA = C(140, 78, 34); p.BtnDownB = C(190, 118, 64); p.Hot = C(255, 190, 110);
                    p.CapA = C(214, 122, 56); p.CapB = C(100, 46, 16); p.CapFrame = C(70, 32, 10); p.CapText = C(255, 232, 190);
                    p.CapBtn1 = C(236, 170, 100); p.CapBtn2 = C(160, 86, 34);
                    p.SelA = C(246, 190, 130); p.SelB = C(196, 118, 56); p.OptA = C(228, 176, 120); p.OptB = C(246, 222, 184);
                    p.ThumbA = C(236, 170, 100); p.ThumbB = C(150, 80, 32); p.Prog1 = C(240, 170, 80); p.Prog2 = C(170, 80, 20);
                    p.Hi = C(255, 232, 186); p.Dim = C(104, 66, 34);
                    p.Rad = 3;
                    break;
                case 5: // Retro / Vintage (Windows 95/98)
                    p.Face = C(192, 192, 192); p.Border = C(0, 0, 0); p.GroupLine = C(128, 128, 128); p.GroupText = C(0, 0, 128);
                    p.Sel = C(0, 0, 128); p.TextBorder = C(128, 128, 128);
                    p.PaneTop = C(0, 128, 128); p.PaneBot = C(0, 128, 128); p.PaneMid = C(0, 128, 128); p.PaneText = Color.White;
                    p.GraphBg = Color.Black; p.GraphGrid = C(0, 100, 0); p.GraphText = Color.Lime;
                    p.OptBorder = Color.Black; p.OptMark = Color.Black;
                    p.Text = Color.Black; p.Dim = C(96, 96, 96); p.Good = C(0, 110, 0); p.Bad = C(190, 0, 0); p.Dis = C(128, 128, 128);
                    p.EditBg = Color.White; p.HitBg = C(255, 190, 190);
                    p.BtnA = C(192, 192, 192); p.BtnB = C(192, 192, 192); p.BtnDownA = C(176, 176, 176); p.BtnDownB = C(176, 176, 176); p.Hot = C(0, 0, 128);
                    p.SelA = C(224, 224, 224); p.SelB = C(208, 208, 208); p.OptA = Color.White; p.OptB = Color.White;
                    p.TrackBg = Color.White; p.Tick = Color.Black; p.ThumbA = C(192, 192, 192); p.ThumbB = C(192, 192, 192); p.ThumbLine = Color.Black;
                    p.CapA = C(0, 0, 128); p.CapB = C(16, 132, 208); p.CapFrame = C(192, 192, 192); p.CapText = Color.White; p.CapShadow = C(0, 0, 128);
                    p.CapBtn1 = C(192, 192, 192); p.CapBtn2 = C(192, 192, 192); p.CapClose1 = C(192, 192, 192); p.CapClose2 = C(192, 192, 192); p.CapX = Color.Black;
                    p.ProgBg = Color.White; p.ProgBorder = C(128, 128, 128); p.Prog1 = C(0, 0, 128); p.Prog2 = C(0, 0, 128);
                    p.SepDark = C(128, 128, 128); p.SepLight = Color.White;
                    p.Rad = 1; p.Bevel = true; p.Square = true;
                    break;
                case 6: // GTA VI (Vice City): por do sol rosa/laranja
                    p.Face = C(58, 20, 84); p.Border = C(255, 110, 150); p.GroupLine = C(255, 120, 160); p.GroupText = C(255, 214, 96);
                    p.Sel = C(220, 60, 130); p.TextBorder = C(160, 74, 128);
                    p.PaneTop = C(255, 110, 60); p.PaneBot = C(90, 20, 110); p.PaneMid = C(180, 60, 110); p.PaneText = C(255, 244, 220);
                    p.GraphBg = C(20, 6, 36); p.GraphGrid = C(84, 32, 96); p.GraphText = C(255, 190, 70);
                    p.OptBorder = C(255, 140, 90); p.OptMark = C(255, 200, 70);
                    p.Text = C(255, 250, 240); p.Dim = C(226, 190, 214); p.Good = C(90, 230, 170); p.Bad = C(255, 96, 116); p.Dis = C(124, 92, 124);
                    p.EditBg = C(34, 10, 54); p.HitBg = C(120, 24, 70);
                    p.BtnA = C(255, 124, 90); p.BtnB = C(190, 40, 120); p.BtnDownA = C(150, 30, 100); p.BtnDownB = C(220, 80, 110); p.Hot = C(255, 214, 90);
                    p.SelA = C(255, 134, 100); p.SelB = C(190, 50, 130); p.OptA = C(90, 38, 112); p.OptB = C(54, 24, 80);
                    p.TrackBg = C(34, 10, 54); p.Tick = C(224, 124, 152); p.ThumbA = C(255, 194, 90); p.ThumbB = C(220, 60, 120); p.ThumbLine = C(255, 244, 220);
                    p.CapA = C(255, 140, 60); p.CapB = C(200, 40, 130); p.CapFrame = C(30, 10, 50); p.CapText = C(255, 246, 230); p.CapShadow = C(90, 10, 60);
                    p.CapBtn1 = C(255, 200, 100); p.CapBtn2 = C(230, 80, 110); p.CapClose1 = C(255, 110, 110); p.CapClose2 = C(190, 30, 60); p.CapX = C(40, 10, 40);
                    p.ProgBg = C(40, 16, 64); p.ProgBorder = C(255, 140, 90); p.Prog1 = C(255, 200, 80); p.Prog2 = C(240, 60, 130);
                    p.SepDark = C(20, 6, 36); p.SepLight = C(150, 70, 130);
                    p.Hi = C(255, 226, 200);
                    p.Rad = 3; p.Dark = true;
                    break;
            }
            return p;
        }
    }

    // ------------------------------------------------------------------
    //  Temas: 0 Luna (XP), 1 Frutiger Aero (Win7), 2 Dark/AMOLED, 3 Neon, 4 Steampunk, 5 Vintage, 6 GTA VI
    // ------------------------------------------------------------------
#pragma warning disable 0649
    static class Xp
    {
        public static readonly string[] ThemeIds = { "luna", "aero", "dark", "neon", "steam", "retro", "gta6" };
        public static readonly string[] ThemeNames = {
            "Luna (XP)", "Frutiger Aero (Win 7)", "Dark / AMOLED",
            "Neon / Cyberpunk", "Steampunk", "Vintage (Win 95)", "GTA VI (Vice City)" };
        public static int Theme;
        public static bool Aero, Bevel, Square, Dark;
        public static int Rad = 3;
        public static Color Face, Border, GroupLine, GroupText, Sel, TextBorder, PaneTop, PaneBot, PaneMid, PaneText;
        public static Color GraphBg, GraphGrid, GraphText, OptBorder, OptMark;
        public static Color Text, Dim, Good, Bad, Dis, EditBg, HitBg, Hi;
        public static Brush HiBr = Brushes.White;
        public static Color BtnA, BtnB, BtnDownA, BtnDownB, Hot, SelA, SelB, OptA, OptB;
        public static Color TrackBg, Tick, ThumbA, ThumbB, ThumbLine;
        public static Color CapA, CapB, CapFrame, CapText, CapShadow, CapBtn1, CapBtn2, CapClose1, CapClose2, CapX;
        public static Color ProgBg, ProgBorder, Prog1, Prog2, SepDark, SepLight;
        public static Font Ui, UiBold, Caption, Big, Title;
        static Font pUi, pBold, pBig, pCap, pTitle;
        static Color pFace;
        static Pal[] pals;
        public static List<Form> Forms = new List<Form>();

        static Xp() { SetTheme(0); }

        public static int IdOf(string id)
        {
            for (int i = 0; i < ThemeIds.Length; i++) if (ThemeIds[i] == id) return i;
            return 0;
        }

        public static void SetTheme(int t)
        {
            if (t < 0 || t >= ThemeIds.Length) t = 0;
            pUi = Ui; pBold = UiBold; pBig = Big; pCap = Caption; pTitle = Title; pFace = Face;
            if (pals == null)
            {
                pals = new Pal[ThemeIds.Length];
                for (int i = 0; i < pals.Length; i++) pals[i] = Pal.Make(i);
            }
            Theme = t; Aero = t == 1;
            Pal p = pals[t];
            foreach (FieldInfo f in typeof(Pal).GetFields())
            {
                FieldInfo d = typeof(Xp).GetField(f.Name, BindingFlags.Public | BindingFlags.Static);
                if (d != null) d.SetValue(null, f.GetValue(p));
            }
            HiBr = new SolidBrush(Hi);
        }

        // Cores "de papel" (texto, destaque, verde, vermelho, cinza) trocam junto com o tema
        static Color Remap(Color c)
        {
            if (c.A == 0 || pals == null) return c;
            int a = c.ToArgb();
            foreach (Pal p in pals)
            {
                if (p.Text.ToArgb() == a) return Text;
                if (p.GroupText.ToArgb() == a) return GroupText;
                if (p.Good.ToArgb() == a) return Good;
                if (p.Bad.ToArgb() == a) return Bad;
                if (p.Dim.ToArgb() == a) return Dim;
            }
            return c;
        }

        // Aplica fontes/cores do tema atual a uma janela e a todos os controles dela
        public static void Apply(Control c)
        {
            if (c.Font == pUi) c.Font = Ui;
            else if (c.Font == pBold) c.Font = UiBold;
            else if (c.Font == pBig) c.Font = Big;
            else if (c.Font == pCap) c.Font = Caption;
            if (c is Form) c.BackColor = Face;
            else if (c is TaskPane) c.BackColor = PaneMid;
            else if (c is XpText) c.BackColor = TextBorder;
            else if (c is ListView || c is ListBox || c is ComboBox || c is TextBoxBase)
            {
                c.BackColor = EditBg; c.ForeColor = Text;
                ComboBox cb = c as ComboBox;
                if (cb != null) cb.FlatStyle = (Dark || Bevel) ? FlatStyle.Flat : FlatStyle.Standard;
                ListView lv = c as ListView;
                if (lv != null)
                    foreach (ListViewItem it in lv.Items)
                    {
                        Color n = Remap(it.ForeColor);
                        if (n != it.ForeColor) it.ForeColor = n;
                    }
            }
            else if (c is Label && (c.Tag as string) == "warn") { c.BackColor = EditBg; c.ForeColor = Text; }
            else if (c.BackColor.A != 0 && c.BackColor.ToArgb() == pFace.ToArgb()) c.BackColor = Face;
            if (!(c is ListView || c is ListBox || c is ComboBox || c is TextBoxBase) && c.ForeColor.A != 0)
            {
                Color n = Remap(c.ForeColor);
                if (n != c.ForeColor) c.ForeColor = n;
            }
            if (c is XpGroup || c is TaskPane || c.GetType() == typeof(Panel)) Fx.Hook(c);
            foreach (Control k in c.Controls) Apply(k);
            XpText xt = c as XpText;
            if (xt != null) xt.Height = xt.Box.PreferredHeight + 4;
            c.Invalidate();
        }

        public static void ApplyAll()
        {
            foreach (Form f in new List<Form>(Forms))
            {
                Apply(f);
                XpForm xf = f as XpForm;
                if (xf != null) xf.RefreshFrame();
            }
        }

        // Relevo estilo Windows 95 (retro)
        public static void Bevel3D(Graphics g, Rectangle r, bool down, Color? fill)
        {
            if (fill.HasValue) using (SolidBrush b = new SolidBrush(fill.Value)) g.FillRectangle(b, r);
            Color hi = Color.White, lo = Color.FromArgb(64, 64, 64), mid = Color.FromArgb(128, 128, 128), lite = Color.FromArgb(223, 223, 223);
            if (down) { Color t = hi; hi = lo; lo = t; t = lite; lite = mid; mid = t; }
            int x0 = r.X, y0 = r.Y, x1 = r.Right - 1, y1 = r.Bottom - 1;
            using (Pen p1 = new Pen(hi)) { g.DrawLine(p1, x0, y0, x1 - 1, y0); g.DrawLine(p1, x0, y0, x0, y1 - 1); }
            using (Pen p2 = new Pen(lo)) { g.DrawLine(p2, x0, y1, x1, y1); g.DrawLine(p2, x1, y0, x1, y1); }
            using (Pen p3 = new Pen(lite)) { g.DrawLine(p3, x0 + 1, y0 + 1, x1 - 2, y0 + 1); g.DrawLine(p3, x0 + 1, y0 + 1, x0 + 1, y1 - 2); }
            using (Pen p4 = new Pen(mid)) { g.DrawLine(p4, x0 + 1, y1 - 1, x1 - 1, y1 - 1); g.DrawLine(p4, x1 - 1, y0 + 1, x1 - 1, y1 - 1); }
        }
        // Botao "vidro" do Windows 7: duas metades com degrade + brilho interno
        public static void Glass(Graphics g, Rectangle r, int rad, Color t1, Color t2, Color b1, Color b2, Color border)
        {
            int h2 = Math.Max(2, r.Height / 2);
            using (GraphicsPath p = Round(r, rad))
            {
                g.SetClip(p);
                using (LinearGradientBrush u = new LinearGradientBrush(new Rectangle(r.X, r.Y, r.Width, h2), t1, t2, 90f))
                    g.FillRectangle(u, r.X, r.Y, r.Width, h2);
                using (LinearGradientBrush l = new LinearGradientBrush(new Rectangle(r.X, r.Y + h2, r.Width, Math.Max(2, r.Height - h2)), b1, b2, 90f))
                    g.FillRectangle(l, r.X, r.Y + h2, r.Width, r.Height - h2);
                g.ResetClip();
                using (Pen bp = new Pen(border)) g.DrawPath(bp, p);
            }
            using (GraphicsPath ip = Round(Rectangle.Inflate(r, -1, -1), Math.Max(1, rad - 1)))
            using (Pen wp = new Pen(Color.FromArgb(140, 255, 255, 255)))
                g.DrawPath(wp, ip);
        }

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            int d = rad * 2, x = r.X, y = r.Y, w = r.Width - 1, h = r.Height - 1;
            GraphicsPath p = new GraphicsPath();
            if (rad <= 0 || r.Width < d + 1 || r.Height < d + 1) { p.AddRectangle(new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h))); return p; }
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Logo: esfera dourada 3D em pixel art (grade 20x20, ampliada sem suavizar)
        public const string Version = "v1.20.3";
        public const int LogoGrid = 20;
        public static Bitmap Logo(int scale)
        {
            int N = LogoGrid;
            Bitmap px = new Bitmap(N, N, PixelFormat.Format32bppArgb);
            double c = (N - 1) / 2.0, R = N / 2.0 - 0.3;
            double lx = -0.5, ly = -0.6, lz = 0.62, ln = Math.Sqrt(lx * lx + ly * ly + lz * lz);
            lx /= ln; ly /= ln; lz /= ln;
            int[,] bayer = { { 0, 2 }, { 3, 1 } };
            Color outline = Color.FromArgb(92, 52, 0);
            Color[] tone = {
                Color.FromArgb(140, 84, 0), Color.FromArgb(184, 116, 4), Color.FromArgb(222, 152, 12),
                Color.FromArgb(248, 190, 36), Color.FromArgb(255, 220, 84), Color.FromArgb(255, 247, 186) };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    if (!InSphere(x, y, c, R)) continue;
                    bool edge = !InSphere(x - 1, y, c, R) || !InSphere(x + 1, y, c, R) || !InSphere(x, y - 1, c, R) || !InSphere(x, y + 1, c, R);
                    if (edge) { px.SetPixel(x, y, outline); continue; }
                    double dx = (x - c) / R, dy = (y - c) / R;
                    double dz = Math.Sqrt(Math.Max(0, 1 - dx * dx - dy * dy));
                    double lam = dx * lx + dy * ly + dz * lz;
                    // luz refletida suave embaixo/direita para dar volume
                    lam += 0.10 * Math.Max(0, dx * 0.6 + dy * 0.7) * (1 - dz);
                    lam += (bayer[y % 2, x % 2] - 1.5) * 0.035;
                    int t = lam < 0.12 ? 0 : lam < 0.34 ? 1 : lam < 0.56 ? 2 : lam < 0.76 ? 3 : lam < 0.90 ? 4 : 5;
                    px.SetPixel(x, y, tone[t]);
                }
            // brilho especular em pixels
            Color hi = Color.FromArgb(255, 255, 235);
            px.SetPixel(5, 5, hi); px.SetPixel(6, 5, hi); px.SetPixel(5, 6, hi);
            px.SetPixel(7, 4, tone[5]); px.SetPixel(4, 7, tone[5]);

            Bitmap b = new Bitmap(N * scale, N * scale);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(px, new Rectangle(0, 0, N * scale, N * scale), 0, 0, N, N, GraphicsUnit.Pixel);
            }
            px.Dispose();
            return b;
        }
        static bool InSphere(int x, int y, double c, double R)
        {
            double dx = x - c, dy = y - c;
            return dx * dx + dy * dy <= R * R;
        }
    }

#pragma warning restore 0649

    // ------------------------------------------------------------------
    //  Controles desenhados no estilo XP
    // ------------------------------------------------------------------
    class XpButton : Control
    {
        bool hot, down;
        public XpButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            Font = Xp.Ui; Size = new Size(90, 24); TabStop = true;
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Xp.Face);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            if (Xp.Aero)
            {
                Color t1, t2, b1, b2, bd;
                if (!Enabled) { t1 = Color.FromArgb(244, 244, 244); t2 = Color.FromArgb(240, 240, 240); b1 = Color.FromArgb(235, 235, 235); b2 = Color.FromArgb(230, 230, 230); bd = Color.FromArgb(173, 178, 181); }
                else if (down) { t1 = Color.FromArgb(196, 229, 246); t2 = Color.FromArgb(172, 219, 243); b1 = Color.FromArgb(146, 206, 237); b2 = Color.FromArgb(104, 177, 225); bd = Color.FromArgb(44, 98, 139); }
                else if (hot) { t1 = Color.FromArgb(234, 246, 253); t2 = Color.FromArgb(217, 240, 252); b1 = Color.FromArgb(190, 230, 253); b2 = Color.FromArgb(167, 217, 245); bd = Color.FromArgb(60, 127, 177); }
                else { t1 = Color.FromArgb(242, 242, 242); t2 = Color.FromArgb(235, 235, 235); b1 = Color.FromArgb(221, 221, 221); b2 = Color.FromArgb(207, 207, 207); bd = Color.FromArgb(112, 112, 112); }
                Xp.Glass(g, r, 3, t1, t2, b1, b2, bd);
                TextRenderer.DrawText(g, Text, Font, r, Enabled ? Color.Black : Xp.Dis,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return;
            }
            if (Xp.Bevel)
            {
                Xp.Bevel3D(g, r, down, down ? Xp.BtnDownA : Xp.BtnA);
                if (Focused && Enabled) using (Pen fp = new Pen(Color.Black) { DashStyle = DashStyle.Dot }) g.DrawRectangle(fp, 4, 4, Width - 9, Height - 9);
                Rectangle tr = down ? new Rectangle(1, 1, Width, Height) : r;
                TextRenderer.DrawText(g, Text, Font, tr, Enabled ? Xp.Text : Xp.Dis, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return;
            }
            Color c1 = down ? Xp.BtnDownA : Xp.BtnA;
            Color c2 = down ? Xp.BtnDownB : Xp.BtnB;
            using (GraphicsPath p = Xp.Round(r, Xp.Rad))
            {
                using (LinearGradientBrush br = new LinearGradientBrush(r, c1, c2, 90f)) g.FillPath(br, p);
                using (Pen pen = new Pen(Enabled ? Xp.Border : Xp.Dis)) g.DrawPath(pen, p);
            }
            if (hot && Enabled && !down)
            {
                using (GraphicsPath ip = Xp.Round(Rectangle.Inflate(r, -2, -2), 2))
                using (Pen hp = new Pen(Xp.Hot, 2f))
                    g.DrawPath(hp, ip);
            }
            TextRenderer.DrawText(g, Text, Font, r, Enabled ? Xp.Text : Xp.Dis,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // Radio / CheckBox estilo XP
    class XpOption : Control
    {
        bool chk, hot;
        public bool Radio;
        public event EventHandler CheckedChanged;
        public XpOption()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Font = Xp.Ui; Size = new Size(200, 18);
            MouseEnter += delegate { hot = true; Invalidate(); };
            MouseLeave += delegate { hot = false; Invalidate(); };
        }
        public bool Checked
        {
            get { return chk; }
            set
            {
                if (chk == value) return;
                chk = value;
                if (value && Radio && Parent != null)
                    foreach (Control c in Parent.Controls)
                    {
                        XpOption o = c as XpOption;
                        if (o != null && o != this && o.Radio) o.Checked = false;
                    }
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }
        protected override void OnClick(EventArgs e) { if (Radio) Checked = true; else Checked = !Checked; base.OnClick(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Xp.Face);
            int y = (Height - 13) / 2;
            Rectangle b = new Rectangle(1, y, 12, 12);
            using (LinearGradientBrush br = new LinearGradientBrush(b, (Xp.Aero && hot) ? Color.FromArgb(190, 230, 253) : Xp.OptA, (Xp.Aero && hot) ? Color.FromArgb(234, 246, 253) : Xp.OptB, 45f))
            {
                if (Radio) g.FillEllipse(br, b); else g.FillRectangle(br, b);
            }
            using (Pen p = new Pen((Xp.Aero && hot) ? Color.FromArgb(60, 127, 177) : Xp.OptBorder))
            {
                if (Radio) g.DrawEllipse(p, b); else g.DrawRectangle(p, b);
            }
            if (chk)
            {
                Color green = Xp.OptMark;
                if (Radio)
                {
                    using (SolidBrush gb = new SolidBrush(green)) g.FillEllipse(gb, b.X + 3, b.Y + 3, 6, 6);
                }
                else
                {
                    using (Pen cp = new Pen(green, 2f))
                        g.DrawLines(cp, new Point[] { new Point(b.X + 2, b.Y + 6), new Point(b.X + 5, b.Y + 9), new Point(b.X + 10, b.Y + 3) });
                }
            }
            TextRenderer.DrawText(g, Text, Font, new Point(19, (Height - Font.Height) / 2), Enabled ? Xp.Text : Xp.Dis);
        }
    }

    // Slider (trackbar) estilo XP
    class XpSlider : Control
    {
        const int PAD = 8;
        int min = 0, max = 100, val = 0;
        bool drag;
        public event EventHandler ValueChanged;
        public XpSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.Selectable, true);
            Height = 34; Width = 300; TabStop = true;
        }
        public int Minimum { get { return min; } set { min = value; if (val < min) val = min; Invalidate(); } }
        public int Maximum { get { return max; } set { max = value; if (val > max) val = max; Invalidate(); } }
        public int Value
        {
            get { return val; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v == val) return;
                val = v; Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }
        void FromX(int x)
        {
            double f = (x - PAD) / (double)Math.Max(1, Width - 2 * PAD);
            Value = min + (int)Math.Round(f * (max - min));
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left && Enabled) { drag = true; Focus(); FromX(e.X); } base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) FromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Invalidate(); base.OnMouseUp(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value = val - 1;
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value = val + 1;
            else if (e.KeyCode == Keys.PageDown) Value = val - 10;
            else if (e.KeyCode == Keys.PageUp) Value = val + 10;
            base.OnKeyDown(e);
        }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Xp.Face);
            int cy = Height / 2, x0 = PAD, x1 = Width - PAD;
            using (SolidBrush tb = new SolidBrush(Xp.TrackBg)) g.FillRectangle(tb, x0, cy - 2, x1 - x0, 4);
            using (Pen d = new Pen(Xp.Tick))
            {
                g.DrawRectangle(d, x0, cy - 2, x1 - x0, 4);
                for (int i = 0; i <= 10; i++)
                {
                    int x = x0 + (x1 - x0) * i / 10;
                    g.DrawLine(d, x, cy + 11, x, cy + 14);
                }
            }
            float px = x0 + (x1 - x0) * (float)(val - min) / Math.Max(1, max - min);
            PointF[] poly = {
                new PointF(px - 5, cy - 11), new PointF(px + 5, cy - 11), new PointF(px + 5, cy + 4),
                new PointF(px, cy + 10), new PointF(px - 5, cy + 4) };
            Color a = drag ? Color.FromArgb(200, 222, 255) : Xp.ThumbA;
            Color b = drag ? Color.FromArgb(140, 180, 240) : Xp.ThumbB;
            if (!Enabled) { a = Xp.BtnDownB; b = Xp.BtnDownA; }
            using (LinearGradientBrush br = new LinearGradientBrush(new RectangleF(px - 5, cy - 11, 10, 21), a, b, 90f))
                g.FillPolygon(br, poly);
            using (Pen p = new Pen(Enabled ? Xp.ThumbLine : Xp.Dis))
                g.DrawPolygon(p, poly);
        }
    }

    // Caixa de texto com borda azul-acinzentada do XP
    class XpText : Panel
    {
        public TextBox Box = new TextBox();
        public XpText()
        {
            BackColor = Xp.TextBorder;
            Padding = new Padding(1);
            Box.BorderStyle = BorderStyle.None;
            Box.Font = Xp.Ui;
            Box.BackColor = Xp.EditBg; Box.ForeColor = Xp.Text;
            Box.Dock = DockStyle.Fill;
            Controls.Add(Box);
            Box.KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.A) { Box.SelectAll(); e.SuppressKeyPress = true; e.Handled = true; }
            };
            Height = Box.PreferredHeight + 4;
            Width = 60;
        }
        public override string Text { get { return Box.Text; } set { Box.Text = value; } }
        public bool ReadOnly { get { return Box.ReadOnly; } set { Box.ReadOnly = value; } }
    }

    // Caixa de grupo estilo XP
    class XpGroup : Panel
    {
        string title = "";
        public string Title { get { return title; } set { title = value; Invalidate(); } }
        public XpGroup() { DoubleBuffered = true; BackColor = Xp.Face; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Fx.Draw(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Xp.Theme == 4) Fx.GroupPipe(g, Width, Height);
            else using (GraphicsPath p = Xp.Round(new Rectangle(0, 8, Width, Height - 8), Xp.Aero ? 6 : 4))
            using (Pen pen = new Pen(Xp.GroupLine))
                g.DrawPath(pen, p);
            if (Xp.Aero)
                using (GraphicsPath p2 = Xp.Round(new Rectangle(1, 9, Width - 2, Height - 10), 5))
                using (Pen wp = new Pen(Color.FromArgb(210, 255, 255, 255)))
                    g.DrawPath(wp, p2);
            Size sz = TextRenderer.MeasureText(title, Xp.UiBold);
            using (SolidBrush b = new SolidBrush(BackColor)) g.FillRectangle(b, 9, 0, sz.Width + 2, 16);
            TextRenderer.DrawText(g, title, Xp.UiBold, new Point(9, 1), Xp.GroupText);
        }
    }

    // Painel lateral (azul Luna ou vidro Aero) - so icones
    class TaskPane : Panel
    {
        static void Bubble(Graphics g, int x, int y, int d)
        {
            using (SolidBrush f = new SolidBrush(Color.FromArgb(38, 255, 255, 255))) g.FillEllipse(f, x, y, d, d);
            using (Pen p = new Pen(Color.FromArgb(150, 255, 255, 255), 1.2f)) g.DrawEllipse(p, x, y, d, d);
            using (SolidBrush h = new SolidBrush(Color.FromArgb(170, 255, 255, 255))) g.FillEllipse(h, x + d * 0.2f, y + d * 0.14f, d * 0.34f, d * 0.22f);
        }
        public TaskPane() { DoubleBuffered = true; BackColor = Xp.PaneMid; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (LinearGradientBrush br = new LinearGradientBrush(ClientRectangle, Xp.PaneTop, Xp.PaneBot, 90f))
                g.FillRectangle(br, ClientRectangle);
            Fx.Draw(this, g);
            if (Xp.Aero)
            {
                // fundo azul/aqua com reflexo de vidro e bolhas (Frutiger Aero)
                using (LinearGradientBrush ag = new LinearGradientBrush(ClientRectangle, Color.White, Color.White, 90f))
                {
                    ColorBlend cb = new ColorBlend(4);
                    cb.Positions = new float[] { 0f, 0.35f, 0.75f, 1f };
                    cb.Colors = new Color[] { Color.FromArgb(216, 238, 253), Color.FromArgb(152, 207, 241), Color.FromArgb(106, 192, 224), Color.FromArgb(122, 208, 192) };
                    ag.InterpolationColors = cb;
                    g.FillRectangle(ag, ClientRectangle);
                }
                using (GraphicsPath gl = new GraphicsPath())
                {
                    gl.AddEllipse(-Width, -Height / 2, Width * 3, Height);
                    using (LinearGradientBrush gb = new LinearGradientBrush(new Rectangle(0, 0, Width, Height / 2 + 2), Color.FromArgb(125, 255, 255, 255), Color.FromArgb(8, 255, 255, 255), 90f))
                        g.FillPath(gb, gl);
                }
                Bubble(g, -22, Height - 150, 112);
                Bubble(g, 30, Height - 70, 40);
                Bubble(g, 40, 430, 24);
                Bubble(g, 6, 190, 18);
                using (Pen edge = new Pen(Color.FromArgb(150, 110, 160, 210))) g.DrawLine(edge, Width - 1, 0, Width - 1, Height);
            }
            if (Xp.Theme == 4) { Fx.HPipe(g, 6, Width - 6, 338, 7); Fx.VPipe(g, Width - 3, 0, Height, 5); }
            else using (Pen dark = new Pen(Xp.SepDark))
            using (Pen light = new Pen(Xp.SepLight))
            {
                g.DrawLine(dark, 10, 338, Width - 10, 338);
                g.DrawLine(light, 10, 339, Width - 10, 339);
            }
            using (Font vf = new Font("Tahoma", 7f))
                TextRenderer.DrawText(g, Xp.Version, vf, new Rectangle(0, Height - 24, Width, 16), Xp.PaneText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // Botao quadrado so com icone (desenhado por codigo)
    class XpIconButton : Control
    {
        public string Kind;
        public bool Selected;
        public int Badge;   // 1 = play, 2 = parar (selo no canto)
        bool hot, down;
        public XpIconButton(string kind, int size)
        {
            Kind = kind;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
            Size = new Size(size, size);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Xp.Face);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            if (Xp.Aero)
            {
                Color t1 = Color.FromArgb(246, 249, 253), t2 = Color.FromArgb(234, 240, 248), b1 = Color.FromArgb(214, 224, 238), b2 = Color.FromArgb(192, 206, 226), bd = Color.FromArgb(128, 144, 172);
                if (Selected) { t1 = Color.FromArgb(206, 230, 252); t2 = Color.FromArgb(176, 212, 246); b1 = Color.FromArgb(132, 188, 238); b2 = Color.FromArgb(96, 164, 226); bd = Color.FromArgb(40, 92, 152); }
                else if (hot) { t1 = Color.FromArgb(238, 249, 255); t2 = Color.FromArgb(220, 242, 253); b1 = Color.FromArgb(190, 232, 253); b2 = Color.FromArgb(166, 218, 246); bd = Color.FromArgb(60, 127, 177); }
                if (down) { t1 = Color.FromArgb(196, 229, 246); t2 = Color.FromArgb(172, 219, 243); b1 = Color.FromArgb(146, 206, 237); b2 = Color.FromArgb(104, 177, 225); bd = Color.FromArgb(44, 98, 139); }
                Xp.Glass(g, r, 5, t1, t2, b1, b2, bd);
            }
            else
            {
                Color c1 = Xp.BtnA, c2 = Xp.BtnB;
                if (Selected) { c1 = Xp.SelA; c2 = Xp.SelB; }
                if (down) { c1 = Xp.BtnDownA; c2 = Xp.BtnDownB; }
                if (Xp.Bevel)
                {
                    Xp.Bevel3D(g, r, down || Selected, Selected && !down ? Color.FromArgb(224, 224, 224) : c1);
                }
                else
                {
                    using (GraphicsPath p = Xp.Round(r, Xp.Rad + 2))
                    {
                        using (LinearGradientBrush br = new LinearGradientBrush(r, c1, c2, 90f)) g.FillPath(br, p);
                        using (Pen pen = new Pen(Selected ? Xp.Sel : Xp.Border, Selected ? 2f : 1f)) g.DrawPath(pen, p);
                    }
                    if (hot && !down)
                        using (GraphicsPath ip = Xp.Round(Rectangle.Inflate(r, -2, -2), 4))
                        using (Pen hp = new Pen(Xp.Hot, 2f))
                            g.DrawPath(hp, ip);
                }
            }
            int pad = Math.Max(3, Width / 6);
            Draw(g, Kind, Rectangle.Inflate(r, -pad, -pad));
            if (Badge != 0)
            {
                float bs = Math.Max(16, Width * 0.42f);
                RectangleF br = new RectangleF(Width - bs - 1, Height - bs - 1, bs, bs);
                using (SolidBrush bb = new SolidBrush(Badge == 1 ? Color.FromArgb(34, 160, 60) : Color.FromArgb(200, 40, 40))) g.FillEllipse(bb, br);
                using (Pen bp = new Pen(Color.White, 1.6f)) g.DrawEllipse(bp, br);
                if (Badge == 1)
                    g.FillPolygon(Brushes.White, new PointF[] { new PointF(br.X + bs * 0.38f, br.Y + bs * 0.27f), new PointF(br.X + bs * 0.38f, br.Y + bs * 0.73f), new PointF(br.X + bs * 0.74f, br.Y + bs * 0.5f) });
                else
                    g.FillRectangle(Brushes.White, br.X + bs * 0.3f, br.Y + bs * 0.3f, bs * 0.4f, bs * 0.4f);
            }
        }

        static void Draw(Graphics g, string kind, Rectangle ic)
        {
            float w = ic.Width, h = ic.Height, cx = ic.X + w / 2f, cy = ic.Y + h / 2f, rad = Math.Min(w, h) / 2f;
            switch (kind)
            {
                case "fan":
                    {
                        Rectangle fb = new Rectangle(ic.X - 1, ic.Y - 1, ic.Width + 2, ic.Height + 2);
                        using (GraphicsPath fp = Xp.Round(fb, 4))
                        {
                            using (LinearGradientBrush fg = new LinearGradientBrush(fb, Color.FromArgb(100, 112, 132), Color.FromArgb(34, 42, 56), 90f)) g.FillPath(fg, fp);
                            using (Pen fpn = new Pen(Color.FromArgb(16, 22, 32))) g.DrawPath(fpn, fp);
                        }
                        using (SolidBrush screw = new SolidBrush(Color.FromArgb(205, 214, 228)))
                        {
                            g.FillEllipse(screw, fb.X + 1.5f, fb.Y + 1.5f, 2.6f, 2.6f);
                            g.FillEllipse(screw, fb.Right - 4.2f, fb.Y + 1.5f, 2.6f, 2.6f);
                            g.FillEllipse(screw, fb.X + 1.5f, fb.Bottom - 4.2f, 2.6f, 2.6f);
                            g.FillEllipse(screw, fb.Right - 4.2f, fb.Bottom - 4.2f, 2.6f, 2.6f);
                        }
                        float r0 = rad * 0.9f;
                        using (SolidBrush hole = new SolidBrush(Color.FromArgb(12, 18, 30))) g.FillEllipse(hole, cx - r0, cy - r0, r0 * 2, r0 * 2);
                        using (Pen ring = new Pen(Color.FromArgb(150, 195, 240), 1.2f)) g.DrawEllipse(ring, cx - r0, cy - r0, r0 * 2, r0 * 2);
                        for (int i = 0; i < 7; i++)
                        {
                            GraphicsState st = g.Save();
                            g.TranslateTransform(cx, cy);
                            g.RotateTransform(i * 360f / 7f);
                            using (GraphicsPath bl = new GraphicsPath())
                            {
                                float hb = r0 * 0.16f, rr = r0 * 0.92f;
                                bl.AddBezier(0, -hb, rr * 0.55f, -rr * 0.2f, rr * 0.6f, -rr * 0.7f, rr * 0.14f, -rr);
                                bl.AddBezier(rr * 0.14f, -rr, -rr * 0.2f, -rr * 0.72f, -rr * 0.16f, -rr * 0.3f, 0, -hb);
                                bl.CloseFigure();
                                using (LinearGradientBrush bb = new LinearGradientBrush(new RectangleF(-rr * 0.3f, -rr, rr * 0.9f, rr), Color.FromArgb(160, 215, 255), Color.FromArgb(20, 90, 210), 60f)) g.FillPath(bb, bl);
                                using (Pen bp = new Pen(Color.FromArgb(10, 40, 110), 0.8f)) g.DrawPath(bp, bl);
                            }
                            g.Restore(st);
                        }
                        RectangleF hub = new RectangleF(cx - r0 * 0.24f, cy - r0 * 0.24f, r0 * 0.48f, r0 * 0.48f);
                        using (LinearGradientBrush hg = new LinearGradientBrush(hub, Color.FromArgb(255, 238, 160), Color.FromArgb(200, 130, 10), 90f)) g.FillEllipse(hg, hub);
                        g.FillEllipse(Xp.HiBr, cx - r0 * 0.1f, cy - r0 * 0.14f, r0 * 0.12f, r0 * 0.1f);
                        break;
                    }
                case "oc":
                    {
                        RectangleF arc = new RectangleF(ic.X, ic.Y + h * 0.18f, w, w);
                        float acy = arc.Y + w / 2f;
                        using (Pen a1 = new Pen(Color.FromArgb(40, 170, 40), 3.5f)) g.DrawArc(a1, arc, 180, 60);
                        using (Pen a2 = new Pen(Color.FromArgb(240, 170, 0), 3.5f)) g.DrawArc(a2, arc, 240, 60);
                        using (Pen a3 = new Pen(Color.FromArgb(220, 40, 30), 3.5f)) g.DrawArc(a3, arc, 300, 60);
                        using (Pen nd = new Pen(Color.FromArgb(30, 30, 30), 2f)) g.DrawLine(nd, cx, acy, cx + w * 0.26f, acy - w * 0.30f);
                        using (SolidBrush hub = new SolidBrush(Color.FromArgb(30, 30, 30))) g.FillEllipse(hub, cx - 3, acy - 3, 6, 6);
                        using (SolidBrush bs = new SolidBrush(Color.FromArgb(0, 60, 116))) g.FillRectangle(bs, ic.X + w * 0.1f, acy + 4, w * 0.8f, 3);
                        break;
                    }
                case "gpu":
                    {
                        float bx = ic.X + w * 0.13f, by = ic.Y + h * 0.06f, bw = w * 0.87f, bh = h * 0.66f;
                        RectangleF card = new RectangleF(bx, by, bw, bh);
                        using (GraphicsPath cp = Xp.Round(Rectangle.Round(card), 3))
                        {
                            using (LinearGradientBrush sh = new LinearGradientBrush(card, Color.FromArgb(82, 90, 108), Color.FromArgb(24, 28, 38), 90f)) g.FillPath(sh, cp);
                            using (Pen sp = new Pen(Color.FromArgb(190, 200, 215), 1f)) g.DrawPath(sp, cp);
                        }
                        using (Pen led = new Pen(Color.FromArgb(0, 214, 255), 1.6f)) g.DrawLine(led, bx + bw * 0.08f, by + 2.6f, bx + bw * 0.92f, by + 2.6f);
                        float fr2 = bh * 0.33f;
                        for (int k = 0; k < 2; k++)
                        {
                            float fx = bx + bw * (k == 0 ? 0.30f : 0.70f), fy = by + bh * 0.55f;
                            using (SolidBrush dark = new SolidBrush(Color.FromArgb(10, 14, 22))) g.FillEllipse(dark, fx - fr2, fy - fr2, fr2 * 2, fr2 * 2);
                            using (Pen rg = new Pen(Color.FromArgb(170, 185, 205), 1f)) g.DrawEllipse(rg, fx - fr2, fy - fr2, fr2 * 2, fr2 * 2);
                            using (SolidBrush bl = new SolidBrush(Color.FromArgb(215, 225, 240)))
                                for (int i = 0; i < 5; i++)
                                {
                                    GraphicsState st = g.Save();
                                    g.TranslateTransform(fx, fy);
                                    g.RotateTransform(i * 72 + k * 20);
                                    g.FillEllipse(bl, -fr2 * 0.16f, -fr2 * 0.92f, fr2 * 0.32f, fr2 * 0.72f);
                                    g.Restore(st);
                                }
                            g.FillEllipse(Brushes.Gold, fx - 1.5f, fy - 1.5f, 3, 3);
                        }
                        using (LinearGradientBrush br2 = new LinearGradientBrush(new RectangleF(ic.X, by - 1, w * 0.1f, bh + 2), Color.Gainsboro, Xp.Dim, 0f))
                            g.FillRectangle(br2, ic.X, by - 1, w * 0.1f, bh + 2);
                        g.FillRectangle(Brushes.Black, ic.X + w * 0.025f, by + bh * 0.2f, w * 0.05f, bh * 0.16f);
                        g.FillRectangle(Brushes.Black, ic.X + w * 0.025f, by + bh * 0.5f, w * 0.05f, bh * 0.16f);
                        using (SolidBrush gold = new SolidBrush(Color.FromArgb(235, 190, 60)))
                            for (int k = 0; k < 9; k++)
                                if (k != 5) g.FillRectangle(gold, bx + bw * 0.08f + k * (bw * 0.075f), by + bh, bw * 0.05f, h * 0.1f);
                        break;
                    }
                case "chip":
                    {
                        RectangleF body = new RectangleF(ic.X + w * 0.18f, ic.Y + h * 0.18f, w * 0.64f, h * 0.64f);
                        using (Pen pin = new Pen(Color.FromArgb(90, 90, 90), 1.5f))
                            for (int i = 0; i < 4; i++)
                            {
                                float fx = body.X + body.Width * (i + 0.5f) / 4f, fy = body.Y + body.Height * (i + 0.5f) / 4f;
                                g.DrawLine(pin, fx, ic.Y, fx, body.Y); g.DrawLine(pin, fx, body.Bottom, fx, ic.Bottom);
                                g.DrawLine(pin, ic.X, fy, body.X, fy); g.DrawLine(pin, body.Right, fy, ic.Right, fy);
                            }
                        using (LinearGradientBrush bb = new LinearGradientBrush(body, Color.FromArgb(90, 105, 135), Color.FromArgb(35, 45, 70), 90f)) g.FillRectangle(bb, body);
                        g.DrawRectangle(Pens.Silver, body.X, body.Y, body.Width, body.Height);
                        g.FillRectangle(Xp.HiBr, cx - w * 0.12f, cy - h * 0.12f, w * 0.24f, h * 0.24f);
                        break;
                    }
                case "chart":
                    {
                        RectangleF scr = new RectangleF(ic.X, ic.Y + h * 0.04f, w, h * 0.7f);
                        g.FillRectangle(Brushes.Black, scr);
                        g.DrawRectangle(Pens.Gray, scr.X, scr.Y, scr.Width, scr.Height);
                        PointF[] pts = { new PointF(scr.X + 2, scr.Bottom - 4), new PointF(scr.X + w * 0.25f, scr.Y + scr.Height * 0.55f), new PointF(scr.X + w * 0.45f, scr.Bottom - 6),
                                         new PointF(scr.X + w * 0.65f, scr.Y + scr.Height * 0.2f), new PointF(scr.X + w * 0.82f, scr.Y + scr.Height * 0.6f), new PointF(scr.Right - 2, scr.Y + 4) };
                        using (Pen ln = new Pen(Color.Lime, 1.6f)) g.DrawLines(ln, pts);
                        g.FillRectangle(Brushes.Gray, cx - w * 0.15f, scr.Bottom + 1, w * 0.3f, h * 0.1f);
                        g.FillRectangle(Brushes.Gray, cx - w * 0.28f, scr.Bottom + 1 + h * 0.1f, w * 0.56f, h * 0.06f);
                        break;
                    }
                case "thermo":
                    {
                        RectangleF tube = new RectangleF(cx - w * 0.11f, ic.Y, w * 0.22f, h * 0.72f);
                        g.FillRectangle(Xp.HiBr, tube);
                        g.DrawRectangle(Pens.DimGray, tube.X, tube.Y, tube.Width, tube.Height);
                        using (SolidBrush red = new SolidBrush(Color.FromArgb(220, 30, 30)))
                        {
                            g.FillRectangle(red, cx - w * 0.05f, ic.Y + h * 0.3f, w * 0.1f, h * 0.45f);
                            g.FillEllipse(red, cx - w * 0.22f, ic.Y + h * 0.62f, w * 0.44f, w * 0.44f);
                        }
                        g.DrawEllipse(Pens.DimGray, cx - w * 0.22f, ic.Y + h * 0.62f, w * 0.44f, w * 0.44f);
                        for (int i = 0; i < 4; i++) g.DrawLine(Pens.DimGray, tube.Right + 2, ic.Y + h * (0.1f + i * 0.14f), tube.Right + 2 + w * 0.15f, ic.Y + h * (0.1f + i * 0.14f));
                        break;
                    }
                case "bench":
                    {
                        float sr = rad * 0.82f, scy = cy + rad * 0.14f;
                        using (SolidBrush btn = new SolidBrush(Color.FromArgb(120, 130, 145))) g.FillRectangle(btn, cx - w * 0.08f, ic.Y, w * 0.16f, h * 0.14f);
                        using (Pen sp = new Pen(Color.FromArgb(120, 130, 145), 2f)) g.DrawLine(sp, cx + sr * 0.7f, scy - sr * 0.75f, cx + sr * 0.95f, scy - sr * 0.95f);
                        RectangleF fr = new RectangleF(cx - sr, scy - sr, sr * 2, sr * 2);
                        using (LinearGradientBrush face = new LinearGradientBrush(fr, Xp.Hi, Color.FromArgb(196, 212, 235), 90f)) g.FillEllipse(face, fr);
                        using (Pen rim = new Pen(Color.FromArgb(40, 80, 160), 2f)) g.DrawEllipse(rim, fr);
                        using (Pen tk = new Pen(Color.FromArgb(90, 100, 120), 1f))
                            for (int i = 0; i < 12; i++)
                            {
                                double a = i * Math.PI / 6;
                                float sn = (float)Math.Sin(a), cs = (float)Math.Cos(a);
                                g.DrawLine(tk, cx + sn * sr * 0.78f, scy - cs * sr * 0.78f, cx + sn * sr * 0.92f, scy - cs * sr * 0.92f);
                            }
                        using (Pen arcp = new Pen(Color.FromArgb(230, 70, 40), 2.2f)) g.DrawArc(arcp, cx - sr * 0.62f, scy - sr * 0.62f, sr * 1.24f, sr * 1.24f, -90, 210);
                        using (Pen hand = new Pen(Color.FromArgb(200, 30, 30), 1.8f)) g.DrawLine(hand, cx, scy, cx + sr * 0.5f, scy - sr * 0.45f);
                        g.FillEllipse(Brushes.DimGray, cx - 2, scy - 2, 4, 4);
                        break;
                    }
                case "cpuoc":
                    {
                        RectangleF body = new RectangleF(ic.X + w * 0.16f, ic.Y + h * 0.16f, w * 0.68f, h * 0.68f);
                        using (Pen pin = new Pen(Color.FromArgb(90, 90, 90), 1.5f))
                            for (int i = 0; i < 4; i++)
                            {
                                float fx = body.X + body.Width * (i + 0.5f) / 4f, fy = body.Y + body.Height * (i + 0.5f) / 4f;
                                g.DrawLine(pin, fx, ic.Y, fx, body.Y); g.DrawLine(pin, fx, body.Bottom, fx, ic.Bottom);
                                g.DrawLine(pin, ic.X, fy, body.X, fy); g.DrawLine(pin, body.Right, fy, ic.Right, fy);
                            }
                        using (LinearGradientBrush bb = new LinearGradientBrush(body, Color.FromArgb(84, 100, 132), Color.FromArgb(30, 40, 66), 90f)) g.FillRectangle(bb, body);
                        g.DrawRectangle(Pens.Silver, body.X, body.Y, body.Width, body.Height);
                        PointF[] bolt = {
                            new PointF(cx + w * 0.12f, ic.Y + h * 0.1f), new PointF(cx - w * 0.16f, cy + h * 0.04f), new PointF(cx - w * 0.01f, cy + h * 0.04f),
                            new PointF(cx - w * 0.12f, ic.Y + h * 0.92f), new PointF(cx + w * 0.18f, cy - h * 0.06f), new PointF(cx + w * 0.03f, cy - h * 0.06f) };
                        using (LinearGradientBrush fb = new LinearGradientBrush(new RectangleF(ic.X, ic.Y, w, h), Color.FromArgb(255, 235, 90), Color.FromArgb(255, 120, 10), 90f)) g.FillPolygon(fb, bolt);
                        using (Pen op = new Pen(Color.FromArgb(120, 50, 0), 1f)) g.DrawPolygon(op, bolt);
                        break;
                    }
                case "ram":
                    {
                        RectangleF pcb = new RectangleF(ic.X, ic.Y + h * 0.2f, w, h * 0.48f);
                        using (LinearGradientBrush pg = new LinearGradientBrush(pcb, Color.FromArgb(40, 160, 84), Color.FromArgb(8, 84, 40), 90f)) g.FillRectangle(pg, pcb);
                        g.DrawRectangle(Pens.DarkGreen, pcb.X, pcb.Y, pcb.Width, pcb.Height);
                        for (int i = 0; i < 4; i++)
                        {
                            RectangleF chip = new RectangleF(ic.X + w * 0.07f + i * (w * 0.225f), pcb.Y + pcb.Height * 0.16f, w * 0.17f, pcb.Height * 0.68f);
                            g.FillRectangle(Brushes.Black, chip);
                            g.FillRectangle(Brushes.DimGray, chip.X + 1, chip.Y + 1, chip.Width - 2, 2);
                        }
                        float pw2 = (w - 4) / 12f;
                        using (SolidBrush gold = new SolidBrush(Color.FromArgb(235, 190, 60)))
                            for (int i = 0; i < 12; i++)
                                if (i != 5) g.FillRectangle(gold, ic.X + 2 + i * pw2, pcb.Bottom, pw2 * 0.62f, h * 0.13f);
                        g.FillRectangle(Brushes.Silver, ic.X - 2, pcb.Y - 3, 3, pcb.Height + 6);
                        g.FillRectangle(Brushes.Silver, ic.Right - 1, pcb.Y - 3, 3, pcb.Height + 6);
                        break;
                    }
                case "net":
                    {
                        RectangleF gb = new RectangleF(ic.X + 1, ic.Y + 1, w - 2, h - 2);
                        using (LinearGradientBrush gbr = new LinearGradientBrush(gb, Color.FromArgb(110, 190, 255), Color.FromArgb(20, 90, 200), 90f)) g.FillEllipse(gbr, gb);
                        using (Pen gp = new Pen(Color.FromArgb(220, 255, 255, 255), 1.3f))
                        {
                            g.DrawEllipse(gp, gb.X + gb.Width * 0.28f, gb.Y, gb.Width * 0.44f, gb.Height);
                            g.DrawLine(gp, gb.X, cy, gb.Right, cy);
                            g.DrawArc(gp, gb.X + gb.Width * 0.06f, gb.Y + gb.Height * 0.2f, gb.Width * 0.88f, gb.Height * 0.6f, 0, 180);
                            g.DrawArc(gp, gb.X + gb.Width * 0.06f, gb.Y + gb.Height * 0.2f, gb.Width * 0.88f, gb.Height * 0.6f, 180, 180);
                        }
                        g.DrawEllipse(Pens.Navy, gb);
                        break;
                    }
                case "health":
                    {
                        RectangleF body = new RectangleF(ic.X, ic.Y + h * 0.1f, w * 0.86f, h * 0.7f);
                        using (LinearGradientBrush bg = new LinearGradientBrush(body, Color.FromArgb(120, 128, 142), Color.FromArgb(52, 58, 70), 90f)) g.FillRectangle(bg, body);
                        g.DrawRectangle(Pens.Black, body.X, body.Y, body.Width, body.Height);
                        using (SolidBrush lb = new SolidBrush(Color.FromArgb(70, 140, 220))) g.FillRectangle(lb, body.X + w * 0.08f, body.Y + body.Height * 0.2f, w * 0.4f, body.Height * 0.4f);
                        RectangleF bd = new RectangleF(ic.X + w * 0.5f, ic.Y + h * 0.42f, w * 0.5f, h * 0.5f);
                        g.FillEllipse(Brushes.White, bd.X - 1, bd.Y - 1, bd.Width + 2, bd.Height + 2);
                        using (SolidBrush gr = new SolidBrush(Color.FromArgb(34, 170, 70))) g.FillEllipse(gr, bd);
                        g.FillRectangle(Brushes.White, bd.X + bd.Width * 0.42f, bd.Y + bd.Height * 0.2f, bd.Width * 0.16f, bd.Height * 0.6f);
                        g.FillRectangle(Brushes.White, bd.X + bd.Width * 0.2f, bd.Y + bd.Height * 0.42f, bd.Width * 0.6f, bd.Height * 0.16f);
                        break;
                    }
                case "opt":
                    {
                        using (Pen hp = new Pen(Color.FromArgb(150, 100, 50), 3f)) { hp.StartCap = LineCap.Round; hp.EndCap = LineCap.Round; g.DrawLine(hp, ic.Right - w * 0.1f, ic.Y + h * 0.05f, ic.X + w * 0.42f, ic.Y + h * 0.58f); }
                        PointF[] br = { new PointF(ic.X + w * 0.3f, ic.Y + h * 0.5f), new PointF(ic.X + w * 0.56f, ic.Y + h * 0.62f), new PointF(ic.X + w * 0.4f, ic.Bottom), new PointF(ic.X + w * 0.02f, ic.Bottom - h * 0.1f) };
                        using (LinearGradientBrush bb = new LinearGradientBrush(new RectangleF(ic.X, ic.Y + h * 0.5f, w * 0.6f, h * 0.5f), Color.FromArgb(240, 200, 70), Color.FromArgb(200, 140, 30), 45f)) g.FillPolygon(bb, br);
                        g.DrawPolygon(Pens.SaddleBrown, br);
                        using (Pen sp = new Pen(Color.FromArgb(255, 255, 255), 1.6f)) { g.DrawLine(sp, ic.X + w * 0.78f, ic.Y + h * 0.62f, ic.X + w * 0.78f, ic.Y + h * 0.84f); g.DrawLine(sp, ic.X + w * 0.67f, ic.Y + h * 0.73f, ic.X + w * 0.89f, ic.Y + h * 0.73f); }
                        break;
                    }
                case "ssd":
                    {
                        RectangleF body = new RectangleF(ic.X, ic.Y + h * 0.14f, w, h * 0.72f);
                        using (LinearGradientBrush bg = new LinearGradientBrush(body, Color.FromArgb(120, 128, 142), Color.FromArgb(52, 58, 70), 90f)) g.FillRectangle(bg, body);
                        g.DrawRectangle(Pens.Black, body.X, body.Y, body.Width, body.Height);
                        RectangleF lab = new RectangleF(body.X + w * 0.1f, body.Y + body.Height * 0.16f, w * 0.52f, body.Height * 0.5f);
                        using (SolidBrush lb = new SolidBrush(Color.FromArgb(70, 140, 220))) g.FillRectangle(lb, lab);
                        g.DrawRectangle(Pens.White, lab.X, lab.Y, lab.Width, lab.Height);
                        using (SolidBrush led = new SolidBrush(Color.FromArgb(60, 230, 90))) g.FillEllipse(led, body.Right - w * 0.2f, body.Bottom - body.Height * 0.3f, w * 0.1f, w * 0.1f);
                        using (SolidBrush gold = new SolidBrush(Color.FromArgb(235, 190, 60)))
                            for (int i = 0; i < 6; i++) g.FillRectangle(gold, body.X + w * 0.08f + i * w * 0.07f, body.Bottom, w * 0.045f, h * 0.1f);
                        break;
                    }
                case "fpu":
                    {
                        using (Font ff = new Font("Arial", h * 0.62f, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Pixel))
                        {
                            g.TextRenderingHint = TextRenderingHint.AntiAlias;
                            using (SolidBrush sb1 = new SolidBrush(Color.FromArgb(40, 90, 190))) g.DrawString("\u03C0", ff, sb1, ic.X - w * 0.02f, ic.Y - h * 0.08f);
                        }
                        using (Pen wp = new Pen(Color.FromArgb(230, 120, 30), 2f))
                        {
                            PointF[] wv = new PointF[16];
                            for (int i = 0; i < 16; i++) wv[i] = new PointF(ic.X + i * w / 15f, ic.Bottom - h * 0.18f - (float)Math.Sin(i * 0.9) * h * 0.14f);
                            g.DrawCurve(wp, wv);
                        }
                        g.DrawRectangle(Pens.SlateGray, ic.X, ic.Y, w, h);
                        break;
                    }
                case "trash":
                    {
                        RectangleF can = new RectangleF(ic.X + w * 0.16f, ic.Y + h * 0.26f, w * 0.68f, h * 0.7f);
                        using (LinearGradientBrush cb = new LinearGradientBrush(can, Color.FromArgb(214, 224, 238), Color.FromArgb(130, 146, 170), 0f)) g.FillRectangle(cb, can);
                        g.DrawRectangle(Pens.DimGray, can.X, can.Y, can.Width, can.Height);
                        using (Pen sl = new Pen(Color.FromArgb(110, 124, 146), 1.4f))
                            for (int i = 1; i < 4; i++) g.DrawLine(sl, can.X + can.Width * i / 4f, can.Y + can.Height * 0.14f, can.X + can.Width * i / 4f, can.Bottom - can.Height * 0.12f);
                        RectangleF lid = new RectangleF(ic.X + w * 0.08f, ic.Y + h * 0.16f, w * 0.84f, h * 0.1f);
                        g.FillRectangle(Brushes.DimGray, lid);
                        g.FillRectangle(Brushes.DimGray, ic.X + w * 0.38f, ic.Y + h * 0.06f, w * 0.24f, h * 0.1f);
                        using (Pen xp = new Pen(Color.FromArgb(210, 40, 40), 2.4f))
                        {
                            g.DrawLine(xp, ic.X + w * 0.66f, ic.Y + h * 0.5f, ic.X + w * 0.94f, ic.Y + h * 0.78f);
                            g.DrawLine(xp, ic.X + w * 0.94f, ic.Y + h * 0.5f, ic.X + w * 0.66f, ic.Y + h * 0.78f);
                        }
                        break;
                    }
                case "aff":
                    {
                        float cs = Math.Min(w, h) / 3.3f, gap = cs * 0.2f;
                        bool[] sel = { true, true, false, false, true, true, true, false, true };
                        for (int r = 0; r < 3; r++)
                            for (int c = 0; c < 3; c++)
                            {
                                RectangleF cell = new RectangleF(ic.X + c * (cs + gap), ic.Y + r * (cs + gap), cs, cs);
                                if (sel[r * 3 + c])
                                {
                                    using (LinearGradientBrush cb = new LinearGradientBrush(cell, Color.FromArgb(120, 180, 255), Color.FromArgb(20, 90, 210), 90f)) g.FillRectangle(cb, cell);
                                    g.DrawRectangle(Pens.Navy, cell.X, cell.Y, cell.Width, cell.Height);
                                    using (Pen ck = new Pen(Xp.Hi, 1.6f))
                                        g.DrawLines(ck, new PointF[] { new PointF(cell.X + cs * 0.22f, cell.Y + cs * 0.52f), new PointF(cell.X + cs * 0.42f, cell.Y + cs * 0.74f), new PointF(cell.X + cs * 0.8f, cell.Y + cs * 0.26f) });
                                }
                                else
                                {
                                    g.FillRectangle(Xp.HiBr, cell);
                                    g.DrawRectangle(Pens.Gray, cell.X, cell.Y, cell.Width, cell.Height);
                                }
                            }
                        break;
                    }
                case "paypal":
                    {
                        using (Font f = new Font("Arial", h * 1.05f, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Pixel))
                        {
                            g.TextRenderingHint = TextRenderingHint.AntiAlias;
                            using (SolidBrush b2 = new SolidBrush(Color.FromArgb(0, 156, 222))) g.DrawString("P", f, b2, ic.X + w * 0.12f, ic.Y - h * 0.14f);
                            using (SolidBrush b1 = new SolidBrush(Color.FromArgb(0, 48, 135))) g.DrawString("P", f, b1, ic.X - w * 0.02f, ic.Y - h * 0.22f);
                        }
                        break;
                    }
                case "livepix":
                    {
                        using (GraphicsPath hp = new GraphicsPath())
                        {
                            hp.AddBezier(cx, ic.Y + h, ic.X - w * 0.15f, ic.Y + h * 0.45f, ic.X + w * 0.1f, ic.Y - h * 0.1f, cx, ic.Y + h * 0.28f);
                            hp.AddBezier(cx, ic.Y + h * 0.28f, ic.X + w * 0.9f, ic.Y - h * 0.1f, ic.X + w * 1.15f, ic.Y + h * 0.45f, cx, ic.Y + h);
                            hp.CloseFigure();
                            using (LinearGradientBrush hb = new LinearGradientBrush(ic, Color.FromArgb(150, 110, 255), Color.FromArgb(90, 50, 200), 90f)) g.FillPath(hb, hp);
                            using (Pen hpn = new Pen(Color.FromArgb(60, 30, 140))) g.DrawPath(hpn, hp);
                        }
                        PointF[] tri = { new PointF(cx - w * 0.1f, cy - h * 0.12f), new PointF(cx - w * 0.1f, cy + h * 0.2f), new PointF(cx + w * 0.16f, cy + h * 0.04f) };
                        g.FillPolygon(Xp.HiBr, tri);
                        break;
                    }
            }
        }
    }

    // Barra de progresso (blocos verdes no XP, vidro verde no Aero)
    class XpProgress : Control
    {
        int val, tick;
        public bool Marquee;
        public XpProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 18;
        }
        public int Value { get { return val; } set { int v = Math.Max(0, Math.Min(100, value)); if (v != val) { val = v; Invalidate(); } } }
        public void Step() { tick++; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Xp.Face);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            if (Xp.Aero)
            {
                using (GraphicsPath p = Xp.Round(r, 3))
                {
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(228, 232, 238))) g.FillPath(bg, p);
                    g.SetClip(p);
                    int fw = Marquee ? Width / 4 : (Width - 2) * val / 100;
                    int fx = Marquee ? (tick * 14) % (Width + Width / 4) - Width / 4 : 1;
                    if (fw > 0)
                    {
                        Rectangle fr = new Rectangle(fx, 1, fw, Height - 2);
                        using (LinearGradientBrush gb = new LinearGradientBrush(fr, Color.FromArgb(170, 240, 130), Color.FromArgb(40, 170, 30), 90f)) g.FillRectangle(gb, fr);
                        using (SolidBrush sh = new SolidBrush(Color.FromArgb(90, 255, 255, 255))) g.FillRectangle(sh, fr.X, fr.Y, fr.Width, fr.Height / 2);
                    }
                    g.ResetClip();
                    using (Pen bp = new Pen(Color.FromArgb(120, 130, 140))) g.DrawPath(bp, p);
                }
            }
            else
            {
                using (SolidBrush pb = new SolidBrush(Xp.ProgBg)) g.FillRectangle(pb, r);
                using (Pen bp = new Pen(Xp.ProgBorder)) g.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);
                int bw = 8, gap = 2, blocks = Math.Max(1, (Width - 6) / (bw + gap));
                int shown = Marquee ? 4 : blocks * val / 100;
                for (int i = 0; i < shown; i++)
                {
                    int bi = Marquee ? (tick + i) % blocks : i;
                    Rectangle b = new Rectangle(3 + bi * (bw + gap), 3, bw, Height - 6);
                    using (LinearGradientBrush gb = new LinearGradientBrush(b, Xp.Prog1, Xp.Prog2, 90f)) g.FillRectangle(gb, b);
                }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Janela base com barra de titulo Luna (azul) desenhada a mao
    // ------------------------------------------------------------------
    class XpForm : Form
    {
        protected const int TB = 30;
        protected virtual void OnMinimizeClick() { WindowState = FormWindowState.Minimized; }
        Bitmap logo = Xp.Logo(1);
        bool hotClose, hotMin;

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        public XpForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Xp.Face; Font = Xp.Ui; DoubleBuffered = true;
            ApplyPadding();
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.FromHandle(Xp.Logo(2).GetHicon());
            Xp.Forms.Add(this);
            FormClosed += delegate { Xp.Forms.Remove(this); };
        }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.Style |= 0x20000; return cp; } // WS_MINIMIZEBOX
        }
        Rectangle CloseR { get { return new Rectangle(Width - 27, 5, 21, 21); } }
        Rectangle MinR { get { return new Rectangle(Width - 51, 5, 21, 21); } }

        void ApplyPadding() { Padding = new Padding(4, TB, 4, 4); }
        protected override void OnLoad(EventArgs e) { base.OnLoad(e); Xp.Apply(this); Lang.Apply(this, null); }
        public void RefreshFrame() { ApplyPadding(); UpdateRegion(); Invalidate(); }
        void UpdateRegion()
        {
            if (Width < 20 || Height < 20) return;
            GraphicsPath p = new GraphicsPath();
            if (Xp.Aero)
            {
                p.AddArc(0, 0, 16, 16, 180, 90);
                p.AddArc(Width - 16, 0, 16, 16, 270, 90);
                p.AddArc(Width - 16, Height - 16, 16, 16, 0, 90);
                p.AddArc(0, Height - 16, 16, 16, 90, 90);
                p.CloseFigure();
            }
            else if (Xp.Square) p.AddRectangle(new Rectangle(0, 0, Width, Height));
            else
            {
                p.AddArc(0, 0, 16, 16, 180, 90);
                p.AddArc(Width - 16, 0, 16, 16, 270, 90);
                p.AddLine(Width, 8, Width, Height);
                p.AddLine(Width, Height, 0, Height);
                p.CloseFigure();
            }
            Region = new Region(p);
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateRegion(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool c = CloseR.Contains(e.Location), m = MinR.Contains(e.Location);
            if (c != hotClose || m != hotMin) { hotClose = c; hotMin = m; Invalidate(new Rectangle(0, 0, Width, TB)); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < TB)
            {
                if (CloseR.Contains(e.Location)) { Close(); return; }
                if (MinR.Contains(e.Location)) { OnMinimizeClick(); return; }
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 2, 0); // WM_NCLBUTTONDOWN, HTCAPTION
            }
            base.OnMouseDown(e);
        }

        void CapLuna(Graphics g, Rectangle r, bool close, bool hot)
        {
            Color c1 = close ? Xp.CapClose1 : Xp.CapBtn1;
            Color c2 = close ? Xp.CapClose2 : Xp.CapBtn2;
            if (hot && !Xp.Bevel) { c1 = ControlPaint.Light(c1); c2 = ControlPaint.Light(c2); }
            if (Xp.Bevel) Xp.Bevel3D(g, r, false, c1);
            else
            using (GraphicsPath p = Xp.Round(r, 3))
            {
                using (LinearGradientBrush br = new LinearGradientBrush(r, c1, c2, 90f)) g.FillPath(br, p);
                using (Pen pen = new Pen(Xp.Theme == 0 ? Color.White : Xp.CapText, 1.5f)) g.DrawPath(pen, p);
            }
            using (Pen x = new Pen(Xp.CapX, 2f))
            {
                if (close)
                {
                    g.DrawLine(x, r.X + 6, r.Y + 6, r.Right - 7, r.Bottom - 7);
                    g.DrawLine(x, r.Right - 7, r.Y + 6, r.X + 6, r.Bottom - 7);
                }
                else g.DrawLine(x, r.X + 6, r.Bottom - 7, r.X + 12, r.Bottom - 7);
            }
        }

        void CapAero(Graphics g, Rectangle r, bool close, bool hot)
        {
            Color t1, t2, b1, b2;
            if (close)
            {
                t1 = hot ? Color.FromArgb(247, 160, 140) : Color.FromArgb(228, 160, 150);
                t2 = hot ? Color.FromArgb(234, 106, 86) : Color.FromArgb(208, 108, 98);
                b1 = hot ? Color.FromArgb(222, 54, 34) : Color.FromArgb(192, 64, 54);
                b2 = hot ? Color.FromArgb(242, 96, 66) : Color.FromArgb(214, 96, 86);
            }
            else
            {
                t1 = hot ? Color.FromArgb(234, 247, 255) : Color.FromArgb(216, 232, 249);
                t2 = hot ? Color.FromArgb(192, 228, 251) : Color.FromArgb(182, 210, 241);
                b1 = hot ? Color.FromArgb(122, 192, 241) : Color.FromArgb(122, 162, 216);
                b2 = hot ? Color.FromArgb(162, 217, 251) : Color.FromArgb(152, 187, 231);
            }
            Xp.Glass(g, new Rectangle(r.X, r.Y - 6, r.Width, r.Height + 6), 5, t1, t2, b1, b2, Color.FromArgb(50, 70, 100));
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            using (Pen dk = new Pen(Color.FromArgb(130, 10, 20, 50), 3.4f))
            using (Pen wh = new Pen(Xp.Hi, 2f))
            {
                if (close)
                {
                    g.DrawLine(dk, cx - 4, cy - 3, cx + 4, cy + 4); g.DrawLine(dk, cx + 4, cy - 3, cx - 4, cy + 4);
                    g.DrawLine(wh, cx - 4, cy - 3, cx + 4, cy + 4); g.DrawLine(wh, cx + 4, cy - 3, cx - 4, cy + 4);
                }
                else
                {
                    g.DrawLine(dk, cx - 4, cy + 3, cx + 4, cy + 3);
                    g.DrawLine(wh, cx - 4, cy + 3, cx + 4, cy + 3);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Xp.Aero) PaintAero(g); else PaintLuna(g);
        }

        void PaintLuna(Graphics g)
        {
            using (SolidBrush fr = new SolidBrush(Xp.CapFrame)) g.FillRectangle(fr, ClientRectangle);
            Rectangle tb = new Rectangle(0, 0, Width, TB);
            if (Xp.Theme == 0)
            {
                using (LinearGradientBrush br = new LinearGradientBrush(tb, Color.FromArgb(9, 151, 255), Color.FromArgb(0, 80, 238), 90f))
                {
                    ColorBlend cb = new ColorBlend(5);
                    cb.Positions = new float[] { 0f, 0.1f, 0.5f, 0.9f, 1f };
                    cb.Colors = new Color[] { Color.FromArgb(9, 151, 255), Color.FromArgb(0, 83, 238), Color.FromArgb(0, 80, 238), Color.FromArgb(0, 96, 250), Color.FromArgb(0, 60, 210) };
                    br.InterpolationColors = cb;
                    g.FillRectangle(br, tb);
                }
            }
            else if (Xp.Bevel)
            {
                Rectangle tb2 = new Rectangle(4, 4, Width - 8, TB - 6);
                using (LinearGradientBrush br = new LinearGradientBrush(tb2, Xp.CapA, Xp.CapB, 0f)) g.FillRectangle(br, tb2);
            }
            else
            {
                using (LinearGradientBrush br = new LinearGradientBrush(tb, Xp.CapA, Xp.CapB, 90f)) g.FillRectangle(br, tb);
                using (Pen hl = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawLine(hl, 8, 1, Width - 8, 1);
            }
            using (SolidBrush fb = new SolidBrush(Xp.Face)) g.FillRectangle(fb, 4, TB, Width - 8, Height - TB - 4);
            g.DrawImageUnscaled(logo, 6, 5);
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            if (!Xp.Bevel) using (SolidBrush sh = new SolidBrush(Xp.CapShadow)) g.DrawString(Text, Xp.Caption, sh, 31, 8);
            using (SolidBrush tw = new SolidBrush(Xp.CapText)) g.DrawString(Text, Xp.Caption, tw, 30, 7);
            CapLuna(g, MinR, false, hotMin);
            CapLuna(g, CloseR, true, hotClose);
            if (Xp.Bevel) Xp.Bevel3D(g, new Rectangle(0, 0, Width, Height), false, null);
            else if (Xp.Theme == 3) using (Pen np = new Pen(Color.FromArgb(200, 255, 60, 220), 1.5f)) g.DrawRectangle(np, 2, TB - 1, Width - 5, Height - TB - 2);
        }

        static void Blob(Graphics g, Rectangle r, Color c)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(r);
                using (PathGradientBrush b = new PathGradientBrush(p))
                {
                    b.CenterColor = c;
                    b.SurroundColors = new Color[] { Color.FromArgb(0, c.R, c.G, c.B) };
                    g.FillPath(b, p);
                }
            }
        }

        void PaintAero(Graphics g)
        {
            Rectangle all = ClientRectangle;
            // vidro: degrade azul-claro + "papel de parede" desfocado por tras
            using (LinearGradientBrush br = new LinearGradientBrush(all, Color.FromArgb(210, 229, 248), Color.FromArgb(160, 192, 232), 90f))
                g.FillRectangle(br, all);
            Blob(g, new Rectangle(-40, -50, 340, 150), Color.FromArgb(150, 90, 205, 235));
            Blob(g, new Rectangle(Width / 2 - 120, -60, 360, 140), Color.FromArgb(110, 120, 190, 255));
            Blob(g, new Rectangle(Width - 330, -40, 340, 140), Color.FromArgb(130, 130, 225, 175));
            Blob(g, new Rectangle(-60, Height - 120, 300, 200), Color.FromArgb(90, 120, 210, 225));
            Blob(g, new Rectangle(Width - 260, Height - 130, 320, 210), Color.FromArgb(90, 150, 225, 160));
            // reflexos diagonais do vidro
            using (GraphicsPath sheen = new GraphicsPath())
            {
                sheen.AddPolygon(new Point[] { new Point(0, 0), new Point(Width * 3 / 5, 0), new Point(Width / 6, Height), new Point(0, Height) });
                using (LinearGradientBrush sb = new LinearGradientBrush(all, Color.FromArgb(105, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0f))
                    g.FillPath(sb, sheen);
            }
            using (GraphicsPath sheen2 = new GraphicsPath())
            {
                sheen2.AddPolygon(new Point[] { new Point(Width * 3 / 4, 0), new Point(Width, 0), new Point(Width, Height / 2), new Point(Width * 2 / 3, Height) });
                using (LinearGradientBrush sb2 = new LinearGradientBrush(all, Color.FromArgb(0, 255, 255, 255), Color.FromArgb(60, 255, 255, 255), 0f))
                    g.FillPath(sb2, sheen2);
            }
            // faixa de brilho na barra de titulo
            using (LinearGradientBrush tg = new LinearGradientBrush(new Rectangle(0, 0, Width, TB), Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                g.FillRectangle(tg, 0, 0, Width, TB);

            Rectangle body = new Rectangle(8, TB, Width - 16, Height - TB - 8);
            using (SolidBrush fb = new SolidBrush(Xp.Face)) g.FillRectangle(fb, body);
            using (Pen ip = new Pen(Color.FromArgb(150, 50, 80, 120))) g.DrawRectangle(ip, body.X - 1, body.Y - 1, body.Width + 1, body.Height + 1);
            using (Pen ip2 = new Pen(Color.FromArgb(190, 255, 255, 255))) g.DrawRectangle(ip2, body.X, body.Y, body.Width - 1, body.Height - 1);
            using (GraphicsPath bp = Xp.Round(new Rectangle(0, 0, Width, Height), 8))
            using (Pen op = new Pen(Color.FromArgb(50, 70, 100))) g.DrawPath(op, bp);
            using (GraphicsPath hp = Xp.Round(new Rectangle(1, 1, Width - 2, Height - 2), 7))
            using (Pen wp = new Pen(Color.FromArgb(170, 255, 255, 255))) g.DrawPath(wp, hp);

            g.DrawImageUnscaled(logo, 8, 5);
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(135, 255, 255, 255)))
                for (int dx = -2; dx <= 2; dx++)
                    for (int dy = -2; dy <= 2; dy++)
                        if ((dx != 0 || dy != 0) && Math.Abs(dx) + Math.Abs(dy) <= 3) g.DrawString(Text, Xp.Caption, glow, 32 + dx, 7 + dy);
            g.DrawString(Text, Xp.Caption, Brushes.Black, 32, 7);
            CapAero(g, MinR, false, hotMin);
            CapAero(g, CloseR, true, hotClose);
        }
    }

    // ------------------------------------------------------------------
    //  Grafico estilo monitor de tarefas (fundo preto, grade verde)
    // ------------------------------------------------------------------
    class Series
    {
        public Sn Sn; public Color Color; public List<float> V = new List<float>();
    }

    class XpGraph : Control
    {
        public const int Store = 3600;               // historico guardado (1 amostra por segundo)
        public List<Series> Items = new List<Series>();
        public bool Normalize = true, Paused;
        public int PeriodMs = 1000;
        int window = 120, hoverX = -1;
        public int Window { get { return window; } set { window = Math.Max(10, value); Invalidate(); } }

        public XpGraph()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = new Font("Tahoma", 7.5f);
            Cursor = Cursors.Cross;
        }
        static bool IsPct(string t) { return t == "Load" || t == "Control" || t == "Level"; }
        static string Secs(double s) { return s >= 120 ? ((int)(s / 60)) + " min" : (s >= 10 ? ((int)s) + " s" : s.ToString("0.0") + " s"); }
        static string Num(float v) { return v.ToString("0.##"); }
        Rectangle Plot() { return new Rectangle(50, 8, Math.Max(10, Width - 60), Math.Max(10, Height - 30)); }

        protected override void OnMouseMove(MouseEventArgs e) { hoverX = e.X; Invalidate(); base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverX = -1; Invalidate(); base.OnMouseLeave(e); }

        void DrawBox(Graphics g, Rectangle area, int x, int y, List<string> rows, List<Color> cols)
        {
            int w = 0;
            foreach (string t in rows) w = Math.Max(w, TextRenderer.MeasureText(t, Font).Width);
            Rectangle b = new Rectangle(x, y, w + 24, rows.Count * 14 + 6);
            if (b.Right > area.Right) b.X = Math.Max(area.Left, area.Right - b.Width);
            if (b.Bottom > area.Bottom) b.Y = Math.Max(area.Top, area.Bottom - b.Height);
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(215, 20, 20, 20))) g.FillRectangle(bg, b);
            g.DrawRectangle(Pens.Gray, b);
            for (int i = 0; i < rows.Count; i++)
            {
                using (SolidBrush sw = new SolidBrush(cols[i])) g.FillRectangle(sw, b.X + 5, b.Y + 6 + i * 14, 8, 8);
                TextRenderer.DrawText(g, rows[i], Font, new Point(b.X + 17, b.Y + 3 + i * 14), cols[i]);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Xp.GraphBg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle pl = Plot();

            float gmax = 0;
            foreach (Series s in Items)
            {
                int n = Math.Min(s.V.Count, window);
                for (int i = s.V.Count - n; i < s.V.Count; i++) gmax = Math.Max(gmax, s.V[i]);
            }
            if (gmax <= 0) gmax = 1;
            gmax *= 1.1f;

            using (Pen gp = new Pen(Xp.GraphGrid))
            {
                for (int i = 0; i <= 10; i++)
                {
                    int y = pl.Top + pl.Height * i / 10;
                    g.DrawLine(gp, pl.Left, y, pl.Right, y);
                    int x = pl.Left + pl.Width * i / 10;
                    g.DrawLine(gp, x, pl.Top, x, pl.Bottom);
                }
            }
            for (int i = 0; i <= 10; i += 2)
            {
                int y = pl.Top + pl.Height * i / 10;
                string lab = Normalize ? (100 - i * 10) + "%" : Num(gmax * (10 - i) / 10f);
                TextRenderer.DrawText(g, lab, Font, new Point(2, y - 6), Xp.GraphText);
            }
            for (int i = 0; i <= 4; i++)
            {
                int x = pl.Left + pl.Width * i / 4;
                string lab = i == 4 ? "agora" : "-" + Secs(window * (4 - i) / 4.0 * PeriodMs / 1000.0);
                TextRenderer.DrawText(g, lab, Font, new Point(x - 14, pl.Bottom + 4), Xp.GraphText);
            }

            float step = pl.Width / (float)(window - 1);
            foreach (Series s in Items)
            {
                int n = Math.Min(s.V.Count, window);
                if (n < 2) continue;
                float scale;
                if (!Normalize) scale = gmax;
                else if (IsPct(s.Sn.Type)) scale = 100f;
                else
                {
                    float m = 0;
                    for (int i = s.V.Count - n; i < s.V.Count; i++) m = Math.Max(m, s.V[i]);
                    scale = m <= 0 ? 1 : m * 1.15f;
                }
                PointF[] pts = new PointF[n];
                for (int i = 0; i < n; i++)
                {
                    float v = s.V[s.V.Count - n + i];
                    float x = pl.Right - (n - 1 - i) * step;
                    float y = pl.Bottom - Math.Max(0f, Math.Min(1f, v / scale)) * pl.Height;
                    pts[i] = new PointF(x, y);
                }
                using (Pen p = new Pen(s.Color, 1.6f)) g.DrawLines(p, pts);
            }

            // legenda com valor atual
            List<string> lt = new List<string>(); List<Color> lc = new List<Color>();
            foreach (Series s in Items)
            {
                if (lt.Count >= 14) break;
                lt.Add(s.Sn.Name + ": " + s.Sn.Fmt(s.Sn.Last)); lc.Add(s.Color);
            }
            if (lt.Count > 0) DrawBox(g, pl, pl.Left + 6, pl.Top + 4, lt, lc);

            // cursor: mostra o valor de cada linha no instante apontado
            if (hoverX >= pl.Left && hoverX <= pl.Right && Items.Count > 0)
            {
                int k = (int)Math.Round((pl.Right - hoverX) / step);
                if (k >= window) k = window - 1;
                float x = pl.Right - k * step;
                using (Pen hp = new Pen(Color.FromArgb(170, 210, 210, 210))) g.DrawLine(hp, x, pl.Top, x, pl.Bottom);
                List<string> rows = new List<string>(); List<Color> cols = new List<Color>();
                rows.Add(k == 0 ? "agora" : "-" + Secs(k * PeriodMs / 1000.0)); cols.Add(Color.Yellow);
                foreach (Series s in Items)
                    if (k < s.V.Count) { rows.Add(s.Sn.Name + ": " + Num(s.V[s.V.Count - 1 - k]) + " " + Sn.Unit(s.Sn.Type)); cols.Add(s.Color); }
                DrawBox(g, pl, (int)x + 10, pl.Bottom - rows.Count * 14 - 12, rows, cols);
            }
            if (Paused) TextRenderer.DrawText(g, Lang.T("PAUSADO"), Xp.UiBold, new Point(pl.Right - 70, pl.Top + 4), Color.Yellow);
            if (Items.Count == 0)
                TextRenderer.DrawText(g, Lang.T("Marque sensores na lista (ou use 'Essenciais') para ver o grafico"), Font, new Point(pl.Left + 20, pl.Top + pl.Height / 2), Xp.GraphText);
        }
    }

    // ------------------------------------------------------------------
    //  NVML (biblioteca oficial da NVIDIA, ja vem com o driver)
    // ------------------------------------------------------------------
    static class Nvml
    {
        const string DLL = "nvml.dll";
        const CallingConvention CC = CallingConvention.Cdecl;
        public static bool Available;

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string f);

        [StructLayout(LayoutKind.Sequential)] struct FanRpmInfo { public uint version; public uint fan; public uint speed; }
        [StructLayout(LayoutKind.Sequential)] struct MemInfo { public ulong total; public ulong free; public ulong used; }
        [StructLayout(LayoutKind.Sequential)] struct UtilInfo { public uint gpu; public uint memory; }
        [StructLayout(LayoutKind.Sequential)] struct Bar1Info { public ulong total; public ulong free; public ulong used; }
        [StructLayout(LayoutKind.Sequential)] struct ClkOff { public uint version; public int type; public int pstate; public int offset; public int min; public int max; }

        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlInit_v2();
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlShutdown();
        [DllImport(DLL, CallingConvention = CC)] static extern IntPtr nvmlErrorString(int r);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlSystemGetDriverVersion(StringBuilder s, uint len);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetCount_v2(out uint c);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetHandleByIndex_v2(uint i, out IntPtr h);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetName(IntPtr h, StringBuilder s, uint len);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetUUID(IntPtr h, StringBuilder s, uint len);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetVbiosVersion(IntPtr h, StringBuilder s, uint len);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetTemperature(IntPtr h, int sensor, out uint t);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetTemperatureThreshold(IntPtr h, int type, out uint t);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetTemperatureThreshold(IntPtr h, int type, ref int t);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetFanSpeed_v2(IntPtr h, uint fan, out uint s);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetNumFans(IntPtr h, out uint n);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetMinMaxFanSpeed(IntPtr h, out uint min, out uint max);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetFanSpeed_v2(IntPtr h, uint fan, uint speed);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetDefaultFanSpeed_v2(IntPtr h, uint fan);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetFanControlPolicy(IntPtr h, uint fan, uint policy);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetFanSpeedRPM(IntPtr h, ref FanRpmInfo i);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetClockInfo(IntPtr h, int type, out uint mhz);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetMaxClockInfo(IntPtr h, int type, out uint mhz);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetPowerUsage(IntPtr h, out uint mw);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetPowerManagementLimit(IntPtr h, out uint mw);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetPowerManagementDefaultLimit(IntPtr h, out uint mw);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetPowerManagementLimitConstraints(IntPtr h, out uint min, out uint max);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetPowerManagementLimit(IntPtr h, uint mw);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetMemoryInfo(IntPtr h, ref MemInfo m);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetUtilizationRates(IntPtr h, ref UtilInfo u);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetMemoryBusWidth(IntPtr h, out uint w);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetNumGpuCores(IntPtr h, out uint n);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetArchitecture(IntPtr h, out uint a);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetCudaComputeCapability(IntPtr h, out int major, out int minor);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetPerformanceState(IntPtr h, out int p);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetCurrPcieLinkGeneration(IntPtr h, out uint v);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetCurrPcieLinkWidth(IntPtr h, out uint v);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetMaxPcieLinkGeneration(IntPtr h, out uint v);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetMaxPcieLinkWidth(IntPtr h, out uint v);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetGpcClkVfOffset(IntPtr h, out int o);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetGpcClkVfOffset(IntPtr h, int o);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetMemClkVfOffset(IntPtr h, out int o);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetMemClkVfOffset(IntPtr h, int o);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetGpcClkMinMaxVfOffset(IntPtr h, out int min, out int max);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetMemClkMinMaxVfOffset(IntPtr h, out int min, out int max);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetGpuLockedClocks(IntPtr h, uint min, uint max);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceResetGpuLockedClocks(IntPtr h);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetMemoryLockedClocks(IntPtr h, uint min, uint max);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceResetMemoryLockedClocks(IntPtr h);

        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetClockOffsets(IntPtr h, ref ClkOff i);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetClockOffsets(IntPtr h, ref ClkOff i);

        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetBAR1MemoryInfo(IntPtr h, ref Bar1Info b);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetDisplayActive(IntPtr h, out int a);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetEccMode(IntPtr h, out int cur, out int pend);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetComputeMode(IntPtr h, out int m);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetGspFirmwareMode(IntPtr h, out uint en, out uint def);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetCurrentClocksThrottleReasons(IntPtr h, out ulong r);

        delegate int GetU(IntPtr h, out uint v);
        delegate int GetI(IntPtr h, out int v);
        delegate int GetS(IntPtr h, StringBuilder sb, uint len);

        static uint? U(GetU f, IntPtr h) { try { uint v; if (f(h, out v) == 0) return v; } catch (Exception) { } return null; }
        static int? I(GetI f, IntPtr h) { try { int v; if (f(h, out v) == 0) return v; } catch (Exception) { } return null; }
        static string S(GetS f, IntPtr h) { try { StringBuilder sb = new StringBuilder(128); if (f(h, sb, 128) == 0) return sb.ToString(); } catch (Exception) { } return null; }
        static int Run(Func<int> f)
        {
            try { return f(); }
            catch (EntryPointNotFoundException) { return -999; }
            catch (DllNotFoundException) { return -998; }
        }

        public static string Err(int code)
        {
            if (code == -999) return "recurso inexistente neste driver";
            if (code == -998) return "nvml.dll nao encontrada";
            try { return Marshal.PtrToStringAnsi(nvmlErrorString(code)) + " (" + code + ")"; } catch (Exception) { return "erro " + code; }
        }

        public static bool Init()
        {
            try
            {
                string[] paths = { Path.Combine(Environment.SystemDirectory, "nvml.dll"), @"C:\Program Files\NVIDIA Corporation\NVSMI\nvml.dll" };
                foreach (string p in paths) if (File.Exists(p) && LoadLibrary(p) != IntPtr.Zero) break;
                Available = nvmlInit_v2() == 0;
            }
            catch (Exception) { Available = false; }
            return Available;
        }
        public static void Shutdown() { if (Available) { try { nvmlShutdown(); } catch (Exception) { } } }

        public static string Driver() { try { StringBuilder sb = new StringBuilder(80); if (nvmlSystemGetDriverVersion(sb, 80) == 0) return sb.ToString(); } catch (Exception) { } return null; }
        public static List<IntPtr> Devices()
        {
            List<IntPtr> l = new List<IntPtr>();
            if (!Available) return l;
            try
            {
                uint n;
                if (nvmlDeviceGetCount_v2(out n) != 0) return l;
                for (uint i = 0; i < n; i++) { IntPtr h; if (nvmlDeviceGetHandleByIndex_v2(i, out h) == 0) l.Add(h); }
            }
            catch (Exception) { }
            return l;
        }
        public static string Name(IntPtr h) { return S(nvmlDeviceGetName, h); }
        public static string Uuid(IntPtr h) { return S(nvmlDeviceGetUUID, h); }
        public static string Vbios(IntPtr h) { return S(nvmlDeviceGetVbiosVersion, h); }

        public static uint? Temp(IntPtr h) { return U(delegate(IntPtr x, out uint o) { return nvmlDeviceGetTemperature(x, 0, out o); }, h); }
        public static uint? TempThr(IntPtr h, int type) { return U(delegate(IntPtr x, out uint o) { return nvmlDeviceGetTemperatureThreshold(x, type, out o); }, h); }
        public static int SetTempThr(IntPtr h, int type, int t) { int v = t; return Run(delegate { return nvmlDeviceSetTemperatureThreshold(h, type, ref v); }); }

        public static uint? Fan(IntPtr h, uint fan) { return U(delegate(IntPtr x, out uint o) { return nvmlDeviceGetFanSpeed_v2(x, fan, out o); }, h); }
        public static uint? NumFans(IntPtr h) { return U(nvmlDeviceGetNumFans, h); }
        public static bool MinMaxFan(IntPtr h, out uint mn, out uint mx)
        {
            mn = 0; mx = 100;
            try { uint a, b; if (nvmlDeviceGetMinMaxFanSpeed(h, out a, out b) == 0) { mn = a; mx = b; return true; } } catch (Exception) { }
            return false;
        }
        public static uint? FanRpm(IntPtr h, uint fan)
        {
            try
            {
                FanRpmInfo i = new FanRpmInfo();
                i.version = (uint)Marshal.SizeOf(typeof(FanRpmInfo)) | (1u << 24);
                i.fan = fan;
                if (nvmlDeviceGetFanSpeedRPM(h, ref i) == 0) return i.speed;
            }
            catch (Exception) { }
            return null;
        }
        public static int SetFan(IntPtr h, uint fan, uint pct) { return Run(delegate { return nvmlDeviceSetFanSpeed_v2(h, fan, pct); }); }
        public static int SetFanDefault(IntPtr h, uint fan) { return Run(delegate { return nvmlDeviceSetDefaultFanSpeed_v2(h, fan); }); }
        public static int SetFanPolicy(IntPtr h, uint fan, uint policy) { return Run(delegate { return nvmlDeviceSetFanControlPolicy(h, fan, policy); }); }

        // tipos: 0 = grafico, 1 = SM, 2 = memoria, 3 = video
        public static uint? Clock(IntPtr h, int type) { return U(delegate(IntPtr x, out uint o) { return nvmlDeviceGetClockInfo(x, type, out o); }, h); }
        public static uint? MaxClock(IntPtr h, int type) { return U(delegate(IntPtr x, out uint o) { return nvmlDeviceGetMaxClockInfo(x, type, out o); }, h); }

        public static uint? Power(IntPtr h) { return U(nvmlDeviceGetPowerUsage, h); }
        public static uint? PowerLimit(IntPtr h) { return U(nvmlDeviceGetPowerManagementLimit, h); }
        public static uint? PowerDefault(IntPtr h) { return U(nvmlDeviceGetPowerManagementDefaultLimit, h); }
        public static bool PowerRange(IntPtr h, out uint mn, out uint mx)
        {
            mn = 0; mx = 0;
            try { uint a, b; if (nvmlDeviceGetPowerManagementLimitConstraints(h, out a, out b) == 0) { mn = a; mx = b; return true; } } catch (Exception) { }
            return false;
        }
        public static int SetPower(IntPtr h, uint mw) { return Run(delegate { return nvmlDeviceSetPowerManagementLimit(h, mw); }); }

        public static bool Mem(IntPtr h, out ulong total, out ulong used)
        {
            total = 0; used = 0;
            try { MemInfo m = new MemInfo(); if (nvmlDeviceGetMemoryInfo(h, ref m) == 0) { total = m.total; used = m.used; return true; } } catch (Exception) { }
            return false;
        }
        public static bool GetUtil(IntPtr h, out uint gpu, out uint mem)
        {
            gpu = 0; mem = 0;
            try { UtilInfo u = new UtilInfo(); if (nvmlDeviceGetUtilizationRates(h, ref u) == 0) { gpu = u.gpu; mem = u.memory; return true; } } catch (Exception) { }
            return false;
        }
        public static uint? MemBus(IntPtr h) { return U(nvmlDeviceGetMemoryBusWidth, h); }
        public static uint? Cores(IntPtr h) { return U(nvmlDeviceGetNumGpuCores, h); }
        public static int? Pstate(IntPtr h) { return I(nvmlDeviceGetPerformanceState, h); }
        public static string Arch(IntPtr h)
        {
            uint? a = U(nvmlDeviceGetArchitecture, h);
            if (!a.HasValue) return null;
            switch (a.Value)
            {
                case 2: return "Kepler"; case 3: return "Maxwell"; case 4: return "Pascal"; case 5: return "Volta";
                case 6: return "Turing"; case 7: return "Ampere"; case 8: return "Ada Lovelace"; case 9: return "Hopper";
                case 10: return "Blackwell"; default: return "Codigo " + a.Value;
            }
        }
        public static string Compute(IntPtr h)
        {
            try { int a, b; if (nvmlDeviceGetCudaComputeCapability(h, out a, out b) == 0) return a + "." + b; } catch (Exception) { }
            return null;
        }
        public static uint? Pcie(IntPtr h, bool width, bool max)
        {
            if (!width && !max) return U(nvmlDeviceGetCurrPcieLinkGeneration, h);
            if (width && !max) return U(nvmlDeviceGetCurrPcieLinkWidth, h);
            if (!width) return U(nvmlDeviceGetMaxPcieLinkGeneration, h);
            return U(nvmlDeviceGetMaxPcieLinkWidth, h);
        }
        public static bool Bar1(IntPtr h, out ulong total, out ulong used)
        {
            total = 0; used = 0;
            try { Bar1Info b = new Bar1Info(); if (nvmlDeviceGetBAR1MemoryInfo(h, ref b) == 0) { total = b.total; used = b.used; return true; } } catch (Exception) { }
            return false;
        }
        public static int? DisplayActive(IntPtr h) { return I(nvmlDeviceGetDisplayActive, h); }
        public static int? ComputeMode(IntPtr h) { return I(nvmlDeviceGetComputeMode, h); }
        public static int? EccCurrent(IntPtr h)
        {
            try { int c, p; if (nvmlDeviceGetEccMode(h, out c, out p) == 0) return c; } catch (Exception) { }
            return null;
        }
        public static int? Gsp(IntPtr h)
        {
            try { uint e, d; if (nvmlDeviceGetGspFirmwareMode(h, out e, out d) == 0) return (int)e; } catch (Exception) { }
            return null;
        }
        public static string Throttle(IntPtr h)
        {
            try
            {
                ulong r;
                if (nvmlDeviceGetCurrentClocksThrottleReasons(h, out r) != 0) return "Indisponivel";
                if (r == 0) return "Nenhuma";
                List<string> l = new List<string>();
                if ((r & 0x1) != 0) l.Add("ociosa (normal)");
                if ((r & 0x2) != 0) l.Add("clock de aplicativo");
                if ((r & 0x4) != 0) l.Add("limite de potencia (SW)");
                if ((r & 0x8) != 0) l.Add("slowdown de hardware");
                if ((r & 0x20) != 0) l.Add("limite termico (SW)");
                if ((r & 0x40) != 0) l.Add("limite termico (HW)");
                if ((r & 0x80) != 0) l.Add("freio de energia (HW)");
                return l.Count == 0 ? "Codigo " + r : string.Join(", ", l.ToArray());
            }
            catch (Exception) { return "Indisponivel"; }
        }

        // API nova (R535+): offsets por tipo de clock (0 = nucleo, 2 = memoria) no estado P0
        public static bool OffNew(IntPtr h, int type, out int cur, out int mn, out int mx)
        {
            cur = 0; mn = 0; mx = 0;
            try
            {
                ClkOff c = new ClkOff();
                c.version = (uint)Marshal.SizeOf(typeof(ClkOff)) | (1u << 24);
                c.type = type; c.pstate = 0;
                if (nvmlDeviceGetClockOffsets(h, ref c) == 0) { cur = c.offset; mn = c.min; mx = c.max; return true; }
            }
            catch (Exception) { }
            return false;
        }
        public static int SetOffsetNew(IntPtr h, int type, int mhz)
        {
            return Run(delegate
            {
                ClkOff c = new ClkOff();
                c.version = (uint)Marshal.SizeOf(typeof(ClkOff)) | (1u << 24);
                c.type = type; c.pstate = 0; c.offset = mhz;
                return nvmlDeviceSetClockOffsets(h, ref c);
            });
        }
        public static int? GpcOffset(IntPtr h)
        {
            int? v = I(nvmlDeviceGetGpcClkVfOffset, h);
            if (v.HasValue) return v;
            int c, a, b;
            return OffNew(h, 0, out c, out a, out b) ? (int?)c : null;
        }
        public static int? MemOffset(IntPtr h)
        {
            int? v = I(nvmlDeviceGetMemClkVfOffset, h);
            if (v.HasValue) return v;
            int c, a, b;
            return OffNew(h, 2, out c, out a, out b) ? (int?)c : null;
        }
        public static bool GpcRange(IntPtr h, out int mn, out int mx)
        {
            mn = -500; mx = 500;
            try { int a, b; if (nvmlDeviceGetGpcClkMinMaxVfOffset(h, out a, out b) == 0 && b > a) { mn = a; mx = b; return true; } } catch (Exception) { }
            int c, x, y;
            if (OffNew(h, 0, out c, out x, out y) && y > x) { mn = x; mx = y; return true; }
            return false;
        }
        public static bool MemRange(IntPtr h, out int mn, out int mx)
        {
            mn = -1000; mx = 1500;
            try { int a, b; if (nvmlDeviceGetMemClkMinMaxVfOffset(h, out a, out b) == 0 && b > a) { mn = a; mx = b; return true; } } catch (Exception) { }
            int c, x, y;
            if (OffNew(h, 2, out c, out x, out y) && y > x) { mn = x; mx = y; return true; }
            return false;
        }
        public static int SetGpcOffset(IntPtr h, int o) { return Run(delegate { return nvmlDeviceSetGpcClkVfOffset(h, o); }); }
        public static int SetMemOffset(IntPtr h, int o) { return Run(delegate { return nvmlDeviceSetMemClkVfOffset(h, o); }); }
        public static int LockGpu(IntPtr h, uint a, uint b) { return Run(delegate { return nvmlDeviceSetGpuLockedClocks(h, a, b); }); }
        public static int UnlockGpu(IntPtr h) { return Run(delegate { return nvmlDeviceResetGpuLockedClocks(h); }); }
        public static int LockMem(IntPtr h, uint a, uint b) { return Run(delegate { return nvmlDeviceSetMemoryLockedClocks(h, a, b); }); }
        public static int UnlockMem(IntPtr h) { return Run(delegate { return nvmlDeviceResetMemoryLockedClocks(h); }); }
    }

    // ------------------------------------------------------------------
    //  NvAPI (Pstates20) - alternativa quando o NVML recusa o offset de clock
    // ------------------------------------------------------------------
    static class NvApi
    {
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr QueryInterface(uint id);
        delegate int D0();
        delegate int DEnum(IntPtr handles, ref uint count);
        delegate int DSet(IntPtr gpu, IntPtr info);
        static bool ready;
        static DEnum enumGpu;
        static DSet setP20;
        const int INFO_SIZE = 7416;   // NV_GPU_PERF_PSTATES20_INFO_V2

        static bool Init(out string err)
        {
            err = null;
            if (ready) return true;
            try
            {
                IntPtr pi = QueryInterface(0x0150E828);
                if (pi == IntPtr.Zero) { err = "NvAPI indisponivel"; return false; }
                D0 init = (D0)Marshal.GetDelegateForFunctionPointer(pi, typeof(D0));
                int r = init();
                if (r != 0) { err = "NvAPI_Initialize erro " + r; return false; }
                IntPtr pe = QueryInterface(0xE5AC921F), ps = QueryInterface(0x0F4DAE6B);
                if (pe == IntPtr.Zero || ps == IntPtr.Zero) { err = "NvAPI sem SetPstates20"; return false; }
                enumGpu = (DEnum)Marshal.GetDelegateForFunctionPointer(pe, typeof(DEnum));
                setP20 = (DSet)Marshal.GetDelegateForFunctionPointer(ps, typeof(DSet));
                ready = true;
                return true;
            }
            catch (Exception ex) { err = "NvAPI: " + ex.Message; return false; }
        }

        static string Name(int r)
        {
            switch (r)
            {
                case -1: return "erro generico"; case -3: return "sem implementacao"; case -5: return "argumento invalido";
                case -9: return "versao de estrutura incompativel"; case -104: return "nao suportado nesta GPU/notebook";
                case -6: return "sem driver NVIDIA"; case -8: return "GPU nao encontrada"; case -110: return "acesso negado (rode como administrador)";
                default: return "codigo " + r;
            }
        }

        // domainId: 0 = nucleo, 4 = memoria. Retorna null se deu certo, ou o erro.
        public static string SetOffset(int gpuIndex, int domainId, int deltaKHz)
        {
            string err;
            if (!Init(out err)) return err;
            IntPtr handles = Marshal.AllocHGlobal(64 * IntPtr.Size);
            IntPtr info = Marshal.AllocHGlobal(INFO_SIZE);
            try
            {
                for (int i = 0; i < 64 * IntPtr.Size; i++) Marshal.WriteByte(handles, i, 0);
                for (int i = 0; i < INFO_SIZE; i += 8) Marshal.WriteInt64(info, i, 0);
                uint cnt = 0;
                int r = enumGpu(handles, ref cnt);
                if (r != 0 || gpuIndex >= (int)cnt) return "NvAPI EnumGPUs erro " + r;
                IntPtr gpu = Marshal.ReadIntPtr(handles, gpuIndex * IntPtr.Size);
                Marshal.WriteInt32(info, 0, INFO_SIZE | (2 << 16)); // versao 2
                Marshal.WriteInt32(info, 8, 1);                      // numPstates
                Marshal.WriteInt32(info, 12, 1);                     // numClocks
                Marshal.WriteInt32(info, 16, 0);                     // numBaseVoltages
                int pst = 20;                                        // pstates[0] = P0
                Marshal.WriteInt32(info, pst, 0);
                int ck = pst + 8;                                    // clocks[0]
                Marshal.WriteInt32(info, ck, domainId);
                Marshal.WriteInt32(info, ck + 4, 0);                 // tipo "single"
                Marshal.WriteInt32(info, ck + 12, deltaKHz);         // freqDelta_kHz.value
                r = setP20(gpu, info);
                if (r == -9 || r == -5)
                {
                    // driver mais antigo: tenta a estrutura versao 1
                    Marshal.WriteInt32(info, 0, 7316 | (1 << 16));
                    r = setP20(gpu, info);
                }
                return r == 0 ? null : "NvAPI SetPstates20 erro " + r + " (" + Name(r) + ")";
            }
            catch (Exception ex) { return "NvAPI: " + ex.Message; }
            finally { Marshal.FreeHGlobal(handles); Marshal.FreeHGlobal(info); }
        }
    }

    // ------------------------------------------------------------------
    //  Sensor generico (vem do LibreHardwareMonitor ou do NVML)
    // ------------------------------------------------------------------
    class Sn
    {
        public string Group, Name, Type;
        public Func<float?> Get;
        public LHM.ISensor Ls;
        public bool IsGpu;
        public string Area
        {
            get
            {
                if (IsGpu) return "GPU";
                if (Group.StartsWith("Processador")) return "CPU";
                if (Group.StartsWith("Placa-mae")) return "Placa-mae";
                if (Group.StartsWith("Memoria RAM")) return "Memoria RAM";
                if (Group.StartsWith("Armazenamento")) return "Discos";
                if (Group.StartsWith("Cooler")) return "Coolers";
                if (Group.StartsWith("Rede")) return "Rede";
                return "Outros";
            }
        }
        public bool InArea(string a) { if (a == "Ventoinhas") return Type == "Fan" || Type == "Control"; return Area == a; }
        // exclui "distancia ate o TjMax", limites e alarmes (nao sao temperaturas reais)
        public bool IsRealTemp
        {
            get
            {
                if (Type != "Temperature") return false;
                string n = Name.ToLower();
                return n.IndexOf("distance") < 0 && n.IndexOf("tjmax") < 0 && n.IndexOf("limit") < 0 && n.IndexOf("critical") < 0 &&
                       n.IndexOf("warning") < 0 && n.IndexOf("target") < 0 && n.IndexOf("threshold") < 0;
            }
        }
        object lastBox;
        public float? Last { get { object o = lastBox; return o == null ? null : (float?)(float)o; } }
        public float Min, Max;
        bool seen;
        public bool Seen { get { return seen; } }
        public float? AlertAbove;
        public DateTime LastBeep = DateTime.MinValue;
        public string Key { get { return Group + "~:~" + Name; } }
        public void ResetMinMax() { float? l = Last; if (l.HasValue) { Min = l.Value; Max = l.Value; } }
        public string Category
        {
            get
            {
                switch (Type)
                {
                    case "Temperature": return "Temperatura";
                    case "Load": case "Level": return "Uso (%)";
                    case "Clock": case "Frequency": return "Clock";
                    case "Power": case "Energy": return "Potencia";
                    case "Voltage": return "Voltagem";
                    case "Current": return "Corrente";
                    case "Fan": return "Ventoinha";
                    case "Control": return "Controle (%)";
                    case "Data": case "SmallData": return "Memoria/Dados";
                    case "Throughput": return "Taxa de dados";
                    default: return "Outros";
                }
            }
        }
        public void Poll()
        {
            float? v = null;
            try { v = Get(); } catch (Exception) { }
            if (v.HasValue && (float.IsNaN(v.Value) || float.IsInfinity(v.Value))) v = null;
            // leituras impossiveis (driver sem acesso ao sensor): 0 C / 0 V / temperatura absurda -> sem leitura
            if (v.HasValue && Type == "Temperature" && (v.Value <= 0f || v.Value > 150f)) v = null;
            if (v.HasValue && Type == "Voltage" && v.Value == 0f) v = null;
            lastBox = v.HasValue ? (object)v.Value : null;
            if (!v.HasValue) return;
            if (!seen) { Min = Max = v.Value; seen = true; }
            else { Min = Math.Min(Min, v.Value); Max = Math.Max(Max, v.Value); }
        }
        public static string Unit(string t)
        {
            switch (t)
            {
                case "Temperature": return "\u00B0C";
                case "Load": case "Control": case "Level": return "%";
                case "Fan": return "RPM";
                case "Clock": return "MHz";
                case "Frequency": return "Hz";
                case "Power": return "W";
                case "Voltage": return "V";
                case "Current": return "A";
                case "Data": return "GB";
                case "SmallData": return "MB";
                case "Throughput": return "B/s";
                case "Flow": return "L/h";
                case "Energy": return "mWh";
                case "Noise": return "dBA";
                case "TimeSpan": return "s";
                default: return "";
            }
        }
        public string Fmt(float? v)
        {
            if (!v.HasValue) return "-";
            return v.Value.ToString("0.##") + " " + Unit(Type);
        }
    }

    // ------------------------------------------------------------------
    //  Uma placa de video
    // ------------------------------------------------------------------
    // ------------------------------------------------------------------
    //  Overclock / ventoinha por fabricante: NVIDIA (NVML+NvAPI), AMD (ADL Overdrive8) e Intel (IGCL)
    // ------------------------------------------------------------------
    class OcCap
    {
        public bool Ok;
        public int Min, Max, Cur, Def;
        public string Unit = "", Why = "";
    }

    abstract class GpuOc
    {
        public string Backend = "", Note = "";
        public OcCap Core = new OcCap(), Mem = new OcCap(), Power = new OcCap(), Temp = new OcCap();
        public bool CanLock, CanFan;
        public abstract void Refresh();
        public abstract string SetCore(int v);
        public abstract string SetMem(int v);
        public abstract string SetPower(int v);
        public abstract string SetTemp(int v);
        public abstract string ResetAll();
        public virtual string SetFan(int pct) { return "Controle de ventoinha indisponivel."; }
        public virtual string FanAuto() { return "Controle de ventoinha indisponivel."; }
        public virtual int? FanPct() { return null; }
        public virtual int? FanRpm() { return null; }
        public static bool IsOk(string s) { return s != null && s.StartsWith("OK"); }
    }

    // ---------- NVIDIA (desktop e notebook) ----------
    class NvOc : GpuOc
    {
        Gpu g; uint defPowerMw; int defTemp;
        public NvOc(Gpu gpu)
        {
            g = gpu; Backend = "NVIDIA (NVML + NvAPI)"; CanLock = true; CanFan = true;
        }
        public override void Refresh()
        {
            int mn, mx;
            bool known = Nvml.GpcRange(g.H, out mn, out mx);
            Core = new OcCap(); Core.Ok = true; Core.Unit = "MHz"; Core.Min = mn; Core.Max = mx; Core.Cur = Nvml.GpcOffset(g.H) ?? 0;
            if (!known) Core.Why = "o driver nao informa a faixa (comum em notebooks); valores acima do limite serao recusados";
            known = Nvml.MemRange(g.H, out mn, out mx);
            Mem = new OcCap(); Mem.Ok = true; Mem.Unit = "MHz"; Mem.Min = mn / Gpu.MemFactor; Mem.Max = mx / Gpu.MemFactor; Mem.Cur = (Nvml.MemOffset(g.H) ?? 0) / Gpu.MemFactor;
            if (!known) Mem.Why = "o driver nao informa a faixa (comum em notebooks); valores acima do limite serao recusados";
            Power = new OcCap(); Power.Unit = "W";
            uint pmn, pmx;
            defPowerMw = Nvml.PowerDefault(g.H) ?? 0;
            if (Nvml.PowerRange(g.H, out pmn, out pmx) && pmx > pmn)
            {
                Power.Ok = true; Power.Min = (int)(pmn / 1000); Power.Max = (int)(pmx / 1000); Power.Def = (int)(defPowerMw / 1000);
                uint? pl = Nvml.PowerLimit(g.H);
                Power.Cur = pl.HasValue ? (int)(pl.Value / 1000) : Power.Max;
            }
            else Power.Why = "limite de potencia fixo nesta placa (normal em notebooks: o fabricante trava o TDP)";
            Temp = new OcCap(); Temp.Unit = "\u00B0C";
            uint? tmin = Nvml.TempThr(g.H, 4), tmax = Nvml.TempThr(g.H, 6), tcur = Nvml.TempThr(g.H, 5);
            if (tcur.HasValue)
            {
                Temp.Ok = true; Temp.Min = tmin.HasValue ? (int)tmin.Value : 60; Temp.Max = tmax.HasValue ? (int)tmax.Value : 95;
                if ((int)tcur.Value > Temp.Max) Temp.Max = (int)tcur.Value;
                if ((int)tcur.Value < Temp.Min) Temp.Min = (int)tcur.Value;
                Temp.Cur = (int)tcur.Value; Temp.Def = Temp.Cur; defTemp = Temp.Cur;
            }
            else Temp.Why = "limite de temperatura nao ajustavel nesta placa";
        }
        static string Hint(string r)
        {
            if (r.StartsWith("FALHOU"))
                return r + "  |  Dica: em notebooks o fabricante (BIOS/vBIOS) pode bloquear o overclock; atualize o driver NVIDIA e tente valores menores. A secao \"Travar clock\" costuma funcionar mesmo assim.";
            return r;
        }
        public override string SetCore(int v)
        {
            string r = Hint(g.SetClockOffset(true, v));
            int? rc = Nvml.GpcOffset(g.H);
            return r.StartsWith("OK") && rc.HasValue ? r + "  [driver le " + rc.Value + "]" : r;
        }
        public override string SetMem(int v)
        {
            string r = Hint(g.SetClockOffset(false, v));
            int? rm = Nvml.MemOffset(g.H);
            return r.StartsWith("OK") && rm.HasValue ? r + "  [driver le " + (rm.Value / Gpu.MemFactor) + "]" : r;
        }
        public override string SetPower(int v)
        {
            if (!Power.Ok) return "nao suportado nesta placa";
            int r = Nvml.SetPower(g.H, (uint)(v * 1000));
            return r == 0 ? "OK" : Nvml.Err(r);
        }
        public override string SetTemp(int v)
        {
            if (!Temp.Ok) return "nao suportado nesta placa";
            int r = Nvml.SetTempThr(g.H, 5, v);
            return r == 0 ? "OK" : Nvml.Err(r);
        }
        public override string ResetAll()
        {
            g.SetClockOffset(true, 0); g.SetClockOffset(false, 0);
            if (defPowerMw > 0) Nvml.SetPower(g.H, defPowerMw);
            if (defTemp > 0) Nvml.SetTempThr(g.H, 5, defTemp);
            Nvml.UnlockGpu(g.H); Nvml.UnlockMem(g.H);
            return "OK";
        }
    }

    // ---------- AMD: biblioteca ADL (atiadlxx.dll, instalada junto do driver Radeon) ----------
    static class Adl
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string f);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)] static extern IntPtr GetProcAddress(IntPtr h, string n);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr DAlloc(int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DCreate(DAlloc cb, int enumConnected, out IntPtr ctx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DNum(IntPtr ctx, out int n);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DInfo(IntPtr ctx, IntPtr buf, int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DCaps(IntPtr ctx, int ad, out int supported, out int enabled, out int version);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DPtr(IntPtr ctx, int ad, IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DPtr2(IntPtr ctx, int ad, IntPtr p, IntPtr q);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DNone(IntPtr ctx, int ad);

        static DAlloc alloc = delegate(int size) { return Marshal.AllocCoTaskMem(size); };
        static bool tried, ok;
        static IntPtr ctx;
        static DNum fNum; static DInfo fInfo; static DCaps fCaps;
        static DPtr fInitGet, fCurGet; static DPtr2 fSet;
        static DPtr fFanGet6, fFanSet6; static DNone fFanReset6;
        public static string Error = "";
        const int INFO_SIZE_DEFAULT = 1572;

        public class Ad { public int Index, Bus, Vendor, Od; public string Name = ""; }
        public static List<Ad> Adapters = new List<Ad>();
        static HashSet<int> usedAd = new HashSet<int>();

        static T Fn<T>(IntPtr lib, string name) where T : class
        {
            IntPtr p = GetProcAddress(lib, name);
            return p == IntPtr.Zero ? null : (Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T);
        }

        static bool Init()
        {
            if (tried) return ok;
            tried = true;
            try
            {
                IntPtr lib = LoadLibrary("atiadlxx.dll");
                if (lib == IntPtr.Zero) lib = LoadLibrary("atiadlxy.dll");
                if (lib == IntPtr.Zero) { Error = "atiadlxx.dll nao encontrada (instale o driver Radeon/Adrenalin)"; return false; }
                DCreate create = Fn<DCreate>(lib, "ADL2_Main_Control_Create");
                fNum = Fn<DNum>(lib, "ADL2_Adapter_NumberOfAdapters_Get");
                fInfo = Fn<DInfo>(lib, "ADL2_Adapter_AdapterInfo_Get");
                fCaps = Fn<DCaps>(lib, "ADL2_Overdrive_Caps");
                fInitGet = Fn<DPtr>(lib, "ADL2_Overdrive8_Init_Setting_Get");
                fCurGet = Fn<DPtr>(lib, "ADL2_Overdrive8_Current_Setting_Get");
                fSet = Fn<DPtr2>(lib, "ADL2_Overdrive8_Setting_Set");
                fFanGet6 = Fn<DPtr>(lib, "ADL2_Overdrive6_FanSpeed_Get");
                fFanSet6 = Fn<DPtr>(lib, "ADL2_Overdrive6_FanSpeed_Set");
                fFanReset6 = Fn<DNone>(lib, "ADL2_Overdrive6_FanSpeed_Reset");
                if (create == null || fNum == null || fInfo == null) { Error = "ADL incompleta"; return false; }
                int r = create(alloc, 0, out ctx);   // 0 = tambem adaptadores sem monitor ligado
                if (r != 0 || ctx == IntPtr.Zero) { ctx = IntPtr.Zero; r = create(alloc, 1, out ctx); }
                if (r != 0 || ctx == IntPtr.Zero) { Error = "ADL2_Main_Control_Create erro " + r; return false; }
                int n;
                if (fNum(ctx, out n) != 0 || n <= 0) { Error = "nenhum adaptador ADL"; return false; }
                int stride = INFO_SIZE_DEFAULT;
                StringBuilder seen = new StringBuilder();
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    int total = n * stride + 8192;
                    IntPtr buf = Marshal.AllocHGlobal(total);
                    try
                    {
                        for (int i = 0; i + 8 <= total; i += 8) Marshal.WriteInt64(buf, i, 0);
                        int ir = fInfo(ctx, buf, n * stride);
                        if (ir != 0) { Error = "AdapterInfo_Get falhou (" + ir + ")"; return false; }
                        int sz = Marshal.ReadInt32(buf, 0);   // iSize da propria estrutura
                        if (attempt == 0 && sz >= 1000 && sz <= 4000 && sz != stride) { stride = sz; continue; }
                        HashSet<int> seenBus = new HashSet<int>();
                        for (int i = 0; i < n; i++)
                        {
                            int bo = i * stride;
                            Ad a = new Ad();
                            a.Index = Marshal.ReadInt32(buf, bo + 4);
                            a.Bus = Marshal.ReadInt32(buf, bo + 8 + 256);
                            a.Vendor = Marshal.ReadInt32(buf, bo + 8 + 256 + 12);
                            a.Name = Marshal.PtrToStringAnsi(new IntPtr(buf.ToInt64() + bo + 8 + 256 + 16));
                            if (a.Name == null) a.Name = "";
                            if (seen.Length < 300) seen.Append(" [" + a.Vendor.ToString("X") + " " + a.Name + "]");
                            bool amd = a.Vendor == 0x1002 || a.Name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0 || a.Name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!amd) continue;
                            if (!seenBus.Add(a.Bus * 1000 + a.Index / 1000)) continue;
                            int sup, en, ver;
                            if (fCaps != null && fCaps(ctx, a.Index, out sup, out en, out ver) == 0) a.Od = sup != 0 ? ver : 0;
                            Adapters.Add(a);
                        }
                        break;
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
                if (Adapters.Count == 0) Error = "nenhum adaptador AMD no ADL (" + n + " adaptador(es):" + seen + ")";
                ok = Adapters.Count > 0;
            }
            catch (Exception ex) { Error = "ADL: " + ex.Message; ok = false; }
            return ok;
        }

        static string Norm(string s) { return Regex.Replace((s ?? "").ToLower().Replace("amd", "").Replace("radeon", "").Replace("(tm)", "").Replace("graphics", ""), "[^a-z0-9]", ""); }

        public static GpuOc Attach(Gpu g, out string why)
        {
            why = "";
            if (!Init()) { why = Error; return null; }
            Ad pick = null;
            string n = Norm(g.Name);
            foreach (Ad a in Adapters)
                if (!usedAd.Contains(a.Index) && n.Length > 0 && (Norm(a.Name).Contains(n) || n.Contains(Norm(a.Name)))) { pick = a; break; }
            if (pick == null) foreach (Ad a in Adapters) if (!usedAd.Contains(a.Index)) { pick = a; break; }
            if (pick == null) { why = "adaptador AMD nao encontrado no ADL"; return null; }
            usedAd.Add(pick.Index);
            AmdOc oc = new AmdOc(pick);
            oc.Refresh();
            return oc;
        }

        // ----- Overdrive8 (RDNA 1/2/3/4) -----
        public const int ID_GFX_MAX = 0, ID_UCLK_MAX = 8, ID_POWER = 9, ID_FAN_MIN = 10, ID_TEMP_MAX = 13, ID_FAN_ZERO = 15;
        const int BUF = 4096;

        public class Od8
        {
            public int Count;
            public int[] Min = new int[64], Max = new int[64], Def = new int[64], Cur = new int[64];
        }

        public static string ReadOd8(Ad a, out Od8 o)
        {
            o = new Od8();
            if (fInitGet == null || fCurGet == null) return "ADL sem Overdrive8";
            IntPtr init = Marshal.AllocHGlobal(BUF), cur = Marshal.AllocHGlobal(BUF);
            try
            {
                int[] tryCounts = { 0, 40, 37, 36, 33, 48, 64 };
                int rc = -1, cnt = 0;
                foreach (int tc in tryCounts)
                {
                    for (int i = 0; i < BUF; i += 8) Marshal.WriteInt64(init, i, 0);
                    Marshal.WriteInt32(init, 0, tc);
                    rc = fInitGet(ctx, a.Index, init);
                    cnt = Marshal.ReadInt32(init, 0);
                    if (rc == 0 && cnt >= 10 && cnt <= 100) break;
                    rc = rc == 0 ? -2 : rc;
                }
                if (rc != 0) return "Overdrive8_Init_Setting_Get erro " + rc;
                o.Count = Math.Min(cnt, 60);
                for (int i = 0; i < BUF; i += 8) Marshal.WriteInt64(cur, i, 0);
                Marshal.WriteInt32(cur, 0, cnt);
                int rc2 = fCurGet(ctx, a.Index, cur);
                for (int i = 0; i < o.Count; i++)
                {
                    int b = 8 + i * 16;
                    o.Min[i] = Marshal.ReadInt32(init, b + 4); o.Max[i] = Marshal.ReadInt32(init, b + 8); o.Def[i] = Marshal.ReadInt32(init, b + 12);
                    o.Cur[i] = rc2 == 0 ? Marshal.ReadInt32(cur, 4 + i * 4) : o.Def[i];
                }
                return null;
            }
            catch (Exception ex) { return "ADL: " + ex.Message; }
            finally { Marshal.FreeHGlobal(init); Marshal.FreeHGlobal(cur); }
        }

        // altera varios ajustes de uma vez; sets = pares (id, valor)
        public static string SetOd8(Ad a, int count, int[] ids, int[] vals)
        {
            if (fSet == null) return "ADL sem Overdrive8_Setting_Set";
            IntPtr set = Marshal.AllocHGlobal(BUF), cur = Marshal.AllocHGlobal(BUF);
            try
            {
                for (int i = 0; i < BUF; i += 8) { Marshal.WriteInt64(set, i, 0); Marshal.WriteInt64(cur, i, 0); }
                Marshal.WriteInt32(set, 0, count);
                Marshal.WriteInt32(cur, 0, count);
                for (int i = 0; i < ids.Length; i++)
                {
                    Marshal.WriteInt32(set, 4 + ids[i] * 8, 1);
                    Marshal.WriteInt32(set, 8 + ids[i] * 8, vals[i]);
                }
                int rc = fSet(ctx, a.Index, set, cur);
                return rc == 0 ? null : "Overdrive8_Setting_Set erro " + rc;
            }
            catch (Exception ex) { return "ADL: " + ex.Message; }
            finally { Marshal.FreeHGlobal(set); Marshal.FreeHGlobal(cur); }
        }

        // ----- Overdrive6 (ventoinha de placas GCN / Polaris) -----
        public static bool HasFan6 { get { return fFanSet6 != null; } }
        public static string FanSet6(Ad a, int pct)
        {
            IntPtr p = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.WriteInt32(p, 0, 1); Marshal.WriteInt32(p, 4, pct); Marshal.WriteInt32(p, 8, 0); Marshal.WriteInt32(p, 12, 0);
                int rc = fFanSet6(ctx, a.Index, p);
                return rc == 0 ? null : "Overdrive6_FanSpeed_Set erro " + rc;
            }
            catch (Exception ex) { return "ADL: " + ex.Message; }
            finally { Marshal.FreeHGlobal(p); }
        }
        public static string FanReset6(Ad a)
        {
            try { if (fFanReset6 == null) return "sem reset"; int rc = fFanReset6(ctx, a.Index); return rc == 0 ? null : "Overdrive6_FanSpeed_Reset erro " + rc; }
            catch (Exception ex) { return "ADL: " + ex.Message; }
        }
        public static int? FanGet6(Ad a)
        {
            if (fFanGet6 == null) return null;
            IntPtr p = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.WriteInt32(p, 0, 1); Marshal.WriteInt32(p, 4, 0); Marshal.WriteInt32(p, 8, 0); Marshal.WriteInt32(p, 12, 0);
                if (fFanGet6(ctx, a.Index, p) == 0) return Marshal.ReadInt32(p, 4);
            }
            catch (Exception) { }
            finally { Marshal.FreeHGlobal(p); }
            return null;
        }
    }

    class AmdOc : GpuOc
    {
        Adl.Ad ad; Adl.Od8 od = new Adl.Od8(); bool od8;
        public AmdOc(Adl.Ad a) { ad = a; Backend = "AMD (ADL Overdrive" + (a.Od > 0 ? " " + a.Od : "") + ")"; }

        static OcCap Range(Adl.Od8 o, int id, bool offset, string unit)
        {
            OcCap c = new OcCap(); c.Unit = unit;
            if (id >= o.Count) return c;
            int mn = o.Min[id], mx = o.Max[id], df = o.Def[id], cu = o.Cur[id];
            if (mx <= mn) return c;
            c.Ok = true;
            if (offset) { c.Min = mn - df; c.Max = mx - df; c.Cur = cu - df; c.Def = 0; }
            else { c.Min = mn; c.Max = mx; c.Cur = cu; c.Def = df; }
            return c;
        }

        public override void Refresh()
        {
            string e = null;
            od8 = false;
            if (ad.Od == 8) { e = Adl.ReadOd8(ad, out od); od8 = e == null; }
            string why = ad.Od == 8 ? "o driver nao liberou este ajuste (" + (e ?? "feature desligada no Adrenalin") + ")"
                : ad.Od == 0 ? "Overdrive indisponivel nesta placa/driver (ative \"Ajuste manual\" no AMD Adrenalin)"
                : "esta placa usa Overdrive " + ad.Od + " (Polaris/Vega/antiga): ajuste de clocks so no AMD Adrenalin; a ventoinha funciona se o driver permitir";
            if (od8)
            {
                Core = Range(od, Adl.ID_GFX_MAX, true, "MHz");
                Mem = Range(od, Adl.ID_UCLK_MAX, true, "MHz");
                Power = Range(od, Adl.ID_POWER, false, "%");
                Temp = Range(od, Adl.ID_TEMP_MAX, false, "\u00B0C");
                OcCap fan = Range(od, Adl.ID_FAN_MIN, false, "%");
                CanFan = fan.Ok;
                Note = "Overdrive8: nucleo = clock maximo; memoria = clock maximo (MHz reais); potencia em % relativo ao padrao.";
            }
            else
            {
                Core = new OcCap(); Mem = new OcCap(); Power = new OcCap(); Temp = new OcCap();
                CanFan = ad.Od == 6 && Adl.HasFan6;
                Note = why;
            }
            if (!Core.Ok) Core.Why = why;
            if (!Mem.Ok) Mem.Why = why;
            if (!Power.Ok) Power.Why = why;
            if (!Temp.Ok) Temp.Why = why;
        }

        string One(int id, int value)
        {
            if (!od8) return "Falhou: Overdrive8 indisponivel";
            string e = Adl.SetOd8(ad, od.Count, new int[] { id }, new int[] { value });
            return e == null ? "OK (ADL)" : "Falhou: " + e + " (confira se o ajuste manual esta liberado no AMD Adrenalin)";
        }
        public override string SetCore(int v) { return Core.Ok ? One(Adl.ID_GFX_MAX, od.Def[Adl.ID_GFX_MAX] + v) : "nao suportado"; }
        public override string SetMem(int v) { return Mem.Ok ? One(Adl.ID_UCLK_MAX, od.Def[Adl.ID_UCLK_MAX] + v) : "nao suportado"; }
        public override string SetPower(int v) { return Power.Ok ? One(Adl.ID_POWER, v) : "nao suportado"; }
        public override string SetTemp(int v) { return Temp.Ok ? One(Adl.ID_TEMP_MAX, v) : "nao suportado"; }

        public override string ResetAll()
        {
            if (!od8) return "Falhou";
            List<int> ids = new List<int>(), vals = new List<int>();
            int[] all = { Adl.ID_GFX_MAX, Adl.ID_UCLK_MAX, Adl.ID_POWER, Adl.ID_TEMP_MAX, Adl.ID_FAN_MIN };
            foreach (int id in all) if (id < od.Count && od.Max[id] > od.Min[id]) { ids.Add(id); vals.Add(od.Def[id]); }
            string e = Adl.SetOd8(ad, od.Count, ids.ToArray(), vals.ToArray());
            return e == null ? "OK" : "Falhou: " + e;
        }

        public override string SetFan(int pct)
        {
            if (od8 && od.Max[Adl.ID_FAN_MIN] > od.Min[Adl.ID_FAN_MIN])
            {
                int v = Math.Max(od.Min[Adl.ID_FAN_MIN], Math.Min(od.Max[Adl.ID_FAN_MIN], pct));
                string e = Adl.SetOd8(ad, od.Count, new int[] { Adl.ID_FAN_MIN }, new int[] { v });
                return e == null ? "OK (ADL Overdrive8): velocidade minima da ventoinha em " + v + "% (a placa pode subir se esquentar)" : "Falhou: " + e;
            }
            if (ad.Od == 6 && Adl.HasFan6)
            {
                string e = Adl.FanSet6(ad, pct);
                return e == null ? "OK (ADL Overdrive6): ventoinha em " + pct + "%" : "Falhou: " + e;
            }
            return "Esta placa AMD nao aceitou controle de ventoinha pelo ADL.";
        }
        public override string FanAuto()
        {
            if (od8 && od.Max[Adl.ID_FAN_MIN] > od.Min[Adl.ID_FAN_MIN])
            {
                string e = Adl.SetOd8(ad, od.Count, new int[] { Adl.ID_FAN_MIN }, new int[] { od.Def[Adl.ID_FAN_MIN] });
                return e == null ? "OK" : "Falhou: " + e;
            }
            if (ad.Od == 6) { string e = Adl.FanReset6(ad); return e == null ? "OK" : "Falhou: " + e; }
            return "Falhou";
        }
        public override int? FanPct()
        {
            if (ad.Od == 6) return Adl.FanGet6(ad);
            return null;
        }
    }

    // ---------- Intel Arc / Graphics: biblioteca IGCL (ControlLib.dll, instalada com o driver Intel) ----------
    static class Igcl
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string f);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)] static extern IntPtr GetProcAddress(IntPtr h, string n);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DInit(IntPtr args, out IntPtr api);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DEnum(IntPtr api, ref uint count, IntPtr devs);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DProps(IntPtr dev, IntPtr props);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DDev(IntPtr dev);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DSetD(IntPtr dev, double v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DGetD(IntPtr dev, out double v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DFans(IntPtr dev, ref uint count, IntPtr fans);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DFanSet(IntPtr fan, IntPtr speed);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DFanDef(IntPtr fan);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DFanGet(IntPtr fan, int units, out int speed);

        static bool tried, ok; static IntPtr api;
        public static string Error = "";
        public static DProps GetProps; public static DDev Waiver, ResetDef;
        public static DSetD SetGpuF, SetVramF, SetPow, SetTemp; public static DGetD GetGpuF, GetVramF, GetPow, GetTemp;
        public static DFans EnumFans; public static DFanSet FanFixed; public static DFanDef FanDefault; public static DFanGet FanGet;
        static List<IntPtr> devs = new List<IntPtr>();
        static HashSet<IntPtr> usedDev = new HashSet<IntPtr>();

        static T Fn<T>(IntPtr lib, string name) where T : class
        {
            IntPtr p = GetProcAddress(lib, name);
            return p == IntPtr.Zero ? null : (Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T);
        }

        static IntPtr FindLib()
        {
            IntPtr h = LoadLibrary("ControlLib.dll");
            if (h != IntPtr.Zero) return h;
            try
            {
                string repo = Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows", @"System32\DriverStore\FileRepository");
                if (Directory.Exists(repo))
                    foreach (string f in Directory.GetFiles(repo, "ControlLib.dll", SearchOption.AllDirectories))
                    {
                        h = LoadLibrary(f);
                        if (h != IntPtr.Zero) return h;
                    }
            }
            catch (Exception) { }
            return IntPtr.Zero;
        }

        static bool Init()
        {
            if (tried) return ok;
            tried = true;
            try
            {
                IntPtr lib = FindLib();
                if (lib == IntPtr.Zero) { Error = "ControlLib.dll nao encontrada (instale o driver Intel Arc / Graphics mais recente)"; return false; }
                DInit init = Fn<DInit>(lib, "ctlInit");
                DEnum en = Fn<DEnum>(lib, "ctlEnumerateDevices");
                GetProps = Fn<DProps>(lib, "ctlOverclockGetProperties");
                Waiver = Fn<DDev>(lib, "ctlOverclockWaiverSet");
                ResetDef = Fn<DDev>(lib, "ctlOverclockResetToDefault");
                SetGpuF = Fn<DSetD>(lib, "ctlOverclockGpuFrequencyOffsetSet"); GetGpuF = Fn<DGetD>(lib, "ctlOverclockGpuFrequencyOffsetGet");
                SetVramF = Fn<DSetD>(lib, "ctlOverclockVramFrequencyOffsetSet"); GetVramF = Fn<DGetD>(lib, "ctlOverclockVramFrequencyOffsetGet");
                SetPow = Fn<DSetD>(lib, "ctlOverclockPowerLimitSet"); GetPow = Fn<DGetD>(lib, "ctlOverclockPowerLimitGet");
                SetTemp = Fn<DSetD>(lib, "ctlOverclockTemperatureLimitSet"); GetTemp = Fn<DGetD>(lib, "ctlOverclockTemperatureLimitGet");
                EnumFans = Fn<DFans>(lib, "ctlEnumFans"); FanFixed = Fn<DFanSet>(lib, "ctlFanSetFixedSpeedMode");
                FanDefault = Fn<DFanDef>(lib, "ctlFanSetDefaultMode"); FanGet = Fn<DFanGet>(lib, "ctlFanGetState");
                if (init == null || en == null) { Error = "IGCL incompleta"; return false; }

                IntPtr args = Marshal.AllocHGlobal(64);
                for (int i = 0; i < 64; i += 8) Marshal.WriteInt64(args, i, 0);
                Marshal.WriteInt32(args, 0, 36);          // Size
                Marshal.WriteByte(args, 4, 0);            // Version
                Marshal.WriteInt32(args, 8, 0x00010000);  // AppVersion 1.0
                uint r = init(args, out api);
                Marshal.FreeHGlobal(args);
                if (r != 0 || api == IntPtr.Zero) { Error = "ctlInit erro 0x" + r.ToString("X"); return false; }
                uint n = 0;
                r = en(api, ref n, IntPtr.Zero);
                if (r != 0 || n == 0 || n > 16) { Error = "nenhum dispositivo Intel (ctlEnumerateDevices 0x" + r.ToString("X") + ")"; return false; }
                IntPtr arr = Marshal.AllocHGlobal((int)n * IntPtr.Size);
                try
                {
                    for (int i = 0; i < (int)n * IntPtr.Size; i++) Marshal.WriteByte(arr, i, 0);
                    r = en(api, ref n, arr);
                    if (r != 0) { Error = "ctlEnumerateDevices 0x" + r.ToString("X"); return false; }
                    for (int i = 0; i < (int)n; i++) { IntPtr d = Marshal.ReadIntPtr(arr, i * IntPtr.Size); if (d != IntPtr.Zero) devs.Add(d); }
                }
                finally { Marshal.FreeHGlobal(arr); }
                ok = devs.Count > 0;
            }
            catch (Exception ex) { Error = "IGCL: " + ex.Message; ok = false; }
            return ok;
        }

        // le o bloco ctl_oc_control_info_t (48 bytes) em offset 'o'
        public static bool Info(IntPtr p, int o, out double mn, out double mx, out double df, out int units)
        {
            mn = mx = df = 0; units = 0;
            if (Marshal.ReadByte(p, o) == 0) return false;
            units = Marshal.ReadInt32(p, o + 4);
            mn = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p, o + 8));
            mx = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p, o + 16));
            df = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p, o + 32));
            return !double.IsNaN(mn) && !double.IsNaN(mx) && mx >= mn && Math.Abs(mn) < 1e7 && Math.Abs(mx) < 1e7;
        }

        public static GpuOc Attach(Gpu g, out string why)
        {
            why = "";
            if (!Init()) { why = Error; return null; }
            if (GetProps == null) { why = "IGCL sem Overclock"; return null; }
            foreach (IntPtr d in devs)
            {
                if (usedDev.Contains(d)) continue;
                IntPtr buf = Marshal.AllocHGlobal(2048);
                try
                {
                    int[] sizes = { 296, 248, 344, 200 };
                    foreach (int sz in sizes)
                    {
                        for (int i = 0; i < 2048; i += 8) Marshal.WriteInt64(buf, i, 0);
                        Marshal.WriteInt32(buf, 0, sz); Marshal.WriteByte(buf, 4, 0);
                        if (GetProps(d, buf) == 0 && Marshal.ReadByte(buf, 5) != 0)
                        {
                            usedDev.Add(d);
                            IntelOc oc = new IntelOc(d, sz);
                            oc.Refresh();
                            return oc;
                        }
                    }
                }
                catch (Exception ex) { why = "IGCL: " + ex.Message; }
                finally { Marshal.FreeHGlobal(buf); }
            }
            if (why.Length == 0) why = "o driver Intel nao liberou overclock para esta GPU (so Arc dedicadas; graficos integrados nao tem)";
            return null;
        }
    }

    class IntelOc : GpuOc
    {
        IntPtr dev; int propSize; bool waived; IntPtr fan; bool fanTried;
        double pScale = 1.0;
        public IntelOc(IntPtr d, int size) { dev = d; propSize = size; Backend = "Intel (IGCL)"; }

        OcCap FromInfo(IntPtr p, int o, string unit, bool isPower)
        {
            OcCap c = new OcCap(); c.Unit = unit;
            double mn, mx, df; int u;
            if (!Igcl.Info(p, o, out mn, out mx, out df, out u)) return c;
            double sc = 1.0;
            if (isPower && u == 10) sc = 1000.0;   // milliwatts -> W
            if (isPower) pScale = sc;
            c.Ok = true; c.Min = (int)Math.Round(mn / sc); c.Max = (int)Math.Round(mx / sc); c.Def = (int)Math.Round(df / sc); c.Cur = c.Def;
            return c;
        }

        public override void Refresh()
        {
            IntPtr buf = Marshal.AllocHGlobal(2048);
            try
            {
                for (int i = 0; i < 2048; i += 8) Marshal.WriteInt64(buf, i, 0);
                Marshal.WriteInt32(buf, 0, propSize); Marshal.WriteByte(buf, 4, 0);
                uint r = Igcl.GetProps(dev, buf);
                if (r == 0)
                {
                    Core = FromInfo(buf, 8, "MHz", false);
                    Mem = FromInfo(buf, 8 + 96, "MHz", false);
                    Power = FromInfo(buf, 8 + 192, "W", true);
                    Temp = FromInfo(buf, 8 + 240, "\u00B0C", false);
                }
            }
            catch (Exception) { }
            finally { Marshal.FreeHGlobal(buf); }
            double v;
            try
            {
                if (Core.Ok && Igcl.GetGpuF != null && Igcl.GetGpuF(dev, out v) == 0) Core.Cur = (int)Math.Round(v);
                if (Mem.Ok && Igcl.GetVramF != null && Igcl.GetVramF(dev, out v) == 0) Mem.Cur = (int)Math.Round(v);
                if (Power.Ok && Igcl.GetPow != null && Igcl.GetPow(dev, out v) == 0) Power.Cur = (int)Math.Round(v / pScale);
                if (Temp.Ok && Igcl.GetTemp != null && Igcl.GetTemp(dev, out v) == 0) Temp.Cur = (int)Math.Round(v);
            }
            catch (Exception) { }
            string why = "este ajuste nao e exposto pelo driver Intel desta GPU";
            if (!Core.Ok) Core.Why = why; if (!Mem.Ok) Mem.Why = why; if (!Power.Ok) Power.Why = why; if (!Temp.Ok) Temp.Why = why;
            Note = "Intel IGCL (experimental): o driver Intel valida os limites; valores fora da faixa sao recusados.";
            TryFan();
        }

        void TryFan()
        {
            if (fanTried) return;
            fanTried = true;
            try
            {
                if (Igcl.EnumFans == null || Igcl.FanFixed == null) return;
                uint n = 0;
                if (Igcl.EnumFans(dev, ref n, IntPtr.Zero) != 0 || n == 0 || n > 8) return;
                IntPtr arr = Marshal.AllocHGlobal((int)n * IntPtr.Size);
                try
                {
                    for (int i = 0; i < (int)n * IntPtr.Size; i++) Marshal.WriteByte(arr, i, 0);
                    if (Igcl.EnumFans(dev, ref n, arr) == 0) { fan = Marshal.ReadIntPtr(arr, 0); CanFan = fan != IntPtr.Zero; }
                }
                finally { Marshal.FreeHGlobal(arr); }
            }
            catch (Exception) { }
        }

        string Waiver()
        {
            if (waived) return null;
            try { if (Igcl.Waiver != null) { uint r = Igcl.Waiver(dev); if (r != 0) return "ctlOverclockWaiverSet 0x" + r.ToString("X"); } }
            catch (Exception ex) { return ex.Message; }
            waived = true; return null;
        }
        string Do(Igcl.DSetD f, double v, string nome)
        {
            string w = Waiver();
            if (w != null) return "Falhou: " + w;
            if (f == null) return "Falhou: funcao ausente";
            try { uint r = f(dev, v); return r == 0 ? "OK (IGCL)" : "Falhou: " + nome + " erro 0x" + r.ToString("X"); }
            catch (Exception ex) { return "Falhou: " + ex.Message; }
        }
        public override string SetCore(int v) { return Core.Ok ? Do(Igcl.SetGpuF, v, "GpuFrequencyOffset") : "nao suportado"; }
        public override string SetMem(int v) { return Mem.Ok ? Do(Igcl.SetVramF, v, "VramFrequencyOffset") : "nao suportado"; }
        public override string SetPower(int v) { return Power.Ok ? Do(Igcl.SetPow, v * pScale, "PowerLimit") : "nao suportado"; }
        public override string SetTemp(int v) { return Temp.Ok ? Do(Igcl.SetTemp, v, "TemperatureLimit") : "nao suportado"; }
        public override string ResetAll()
        {
            try { if (Igcl.ResetDef != null && Igcl.ResetDef(dev) == 0) return "OK"; } catch (Exception) { }
            string a = SetCore(0), b = SetMem(0);
            return GpuOc.IsOk(a) || GpuOc.IsOk(b) ? "OK" : "Falhou";
        }
        public override string SetFan(int pct)
        {
            TryFan();
            if (fan == IntPtr.Zero) return "A placa Intel nao expoe controle de ventoinha.";
            IntPtr p = Marshal.AllocHGlobal(32);
            try
            {
                for (int i = 0; i < 32; i += 8) Marshal.WriteInt64(p, i, 0);
                Marshal.WriteInt32(p, 0, 16); Marshal.WriteInt32(p, 8, pct); Marshal.WriteInt32(p, 12, 1); // 1 = percentual
                uint r = Igcl.FanFixed(fan, p);
                return r == 0 ? "OK (IGCL): ventoinha em " + pct + "%" : "Falhou: ctlFanSetFixedSpeedMode erro 0x" + r.ToString("X");
            }
            catch (Exception ex) { return "Falhou: " + ex.Message; }
            finally { Marshal.FreeHGlobal(p); }
        }
        public override string FanAuto()
        {
            if (fan == IntPtr.Zero || Igcl.FanDefault == null) return "Falhou";
            try { return Igcl.FanDefault(fan) == 0 ? "OK" : "Falhou"; } catch (Exception) { return "Falhou"; }
        }
        public override int? FanPct()
        {
            if (fan == IntPtr.Zero || Igcl.FanGet == null) return null;
            try { int s; if (Igcl.FanGet(fan, 1, out s) == 0) return s; } catch (Exception) { }
            return null;
        }
        public override int? FanRpm()
        {
            if (fan == IntPtr.Zero || Igcl.FanGet == null) return null;
            try { int s; if (Igcl.FanGet(fan, 0, out s) == 0) return s; } catch (Exception) { }
            return null;
        }
    }

    // VRAM e versao do driver direto do registro (WMI limita em 4 GB)
    static class GpuReg
    {
        public static void Read(string name, out long vram, out string driver, out string driverDate)
        {
            vram = 0; driver = null; driverDate = null;
            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls == null) return;
                    foreach (string sk in cls.GetSubKeyNames())
                    {
                        if (sk.Length != 4) continue;
                        using (RegistryKey k = cls.OpenSubKey(sk))
                        {
                            if (k == null) continue;
                            string desc = Convert.ToString(k.GetValue("DriverDesc"));
                            if (string.IsNullOrEmpty(desc) || !(desc.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf(desc, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                            object q = k.GetValue("HardwareInformation.qwMemorySize");
                            if (q != null) { try { vram = Convert.ToInt64(q); } catch (Exception) { } }
                            if (vram == 0)
                            {
                                byte[] b = k.GetValue("HardwareInformation.MemorySize") as byte[];
                                if (b != null && b.Length >= 4) vram = BitConverter.ToUInt32(b, 0);
                            }
                            driver = Convert.ToString(k.GetValue("DriverVersion"));
                            driverDate = Convert.ToString(k.GetValue("DriverDate"));
                            return;
                        }
                    }
                }
            }
            catch (Exception) { }
        }
    }

    class Gpu
    {
        public string Name, Vendor;
        public LHM.IHardware Hw;
        public IntPtr H;
        public bool Nv;
        public int NvIndex;
        public GpuOc Oc;          // backend de OC/ventoinha (NVIDIA, AMD ou Intel)
        public string OcWhy = ""; // por que nao ha backend

        public void AttachOc()
        {
            try
            {
                if (Nv) { Oc = new NvOc(this); return; }
                string why;
                if (Settings.Get("vendoroc", "1") == "0") { OcWhy = "desligado no .ini (vendoroc=0)"; return; }
                if (Vendor == "AMD") Oc = Adl.Attach(this, out why);
                else if (Vendor == "Intel") Oc = Igcl.Attach(this, out why);
                else { why = "fabricante sem suporte de OC nesta versao"; }
                OcWhy = why ?? "";
            }
            catch (Exception ex) { OcWhy = ex.Message; Oc = null; }
        }
        // O driver NVIDIA expressa o offset da memoria em taxa efetiva (2x o clock mostrado no monitor).
        // O programa converte: o usuario digita MHz reais e o driver recebe o dobro. (Mude para 1 se quiser o valor cru.)
        public const int MemFactor = 2;

        public bool CanFan
        {
            get
            {
                if (Nv) return true;
                if (Hw != null) foreach (LHM.ISensor x in Hw.Sensors) if (x.SensorType.ToString() == "Control" && x.Control != null) return true;
                return Oc != null && Oc.CanFan;
            }
        }
        public override string ToString() { return Name + (Vendor == "NVIDIA" ? "" : "  [" + Vendor + "]"); }

        public float? Lhm(string type, params string[] names)
        {
            if (Hw == null) return null;
            foreach (LHM.ISensor s in Hw.Sensors)
            {
                if (s.SensorType.ToString() != type || !s.Value.HasValue) continue;
                if (names.Length == 0) return s.Value;
                foreach (string n in names)
                    if (s.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return s.Value;
            }
            return null;
        }
        static float? F(uint? v) { return v.HasValue ? (float?)v.Value : null; }

        public int? Temp()
        {
            if (Nv) { uint? t = Nvml.Temp(H); if (t.HasValue) return (int)t.Value; }
            float? f = Lhm("Temperature", "Core");
            if (!f.HasValue) f = Lhm("Temperature");
            return f.HasValue ? (int?)Math.Round(f.Value) : null;
        }
        public int? FanPct()
        {
            if (Nv) { uint? v = Nvml.Fan(H, 0); if (v.HasValue) return (int)v.Value; }
            float? f = Lhm("Control", "Fan");
            if (f.HasValue) return (int?)Math.Round(f.Value);
            return Oc != null ? Oc.FanPct() : null;
        }
        public int? FanRpm()
        {
            if (Nv) { uint? v = Nvml.FanRpm(H, 0); if (v.HasValue) return (int)v.Value; }
            float? f = Lhm("Fan");
            if (f.HasValue) return (int?)Math.Round(f.Value);
            return Oc != null ? Oc.FanRpm() : null;
        }
        public int FanCount()
        {
            if (Nv) { uint? n = Nvml.NumFans(H); if (n.HasValue && n.Value > 0) return (int)n.Value; }
            int c = 0;
            if (Hw != null) foreach (LHM.ISensor s in Hw.Sensors) if (s.SensorType.ToString() == "Fan") c++;
            return c;
        }
        public float? PowerW()
        {
            if (Nv) { uint? p = Nvml.Power(H); if (p.HasValue) return p.Value / 1000f; }
            return Lhm("Power");
        }
        public float? Clk(int type, string lhmName)
        {
            if (Nv) { uint? c = Nvml.Clock(H, type); if (c.HasValue) return c.Value; }
            return Lhm("Clock", lhmName);
        }

        public bool SetFan(int pct, out string msg)
        {
            pct = Math.Max(0, Math.Min(100, pct));
            string nvErr = null;
            if (Nv)
            {
                int n = Math.Max(1, FanCount());
                bool all = true; int last = 0;
                for (int f = 0; f < n; f++)
                {
                    Nvml.SetFanPolicy(H, (uint)f, 1); // 1 = manual
                    int r = Nvml.SetFan(H, (uint)f, (uint)pct);
                    if (r != 0) { all = false; last = r; }
                }
                if (all) { msg = "OK (NVML): ventoinha(s) em " + pct + "%"; return true; }
                nvErr = Nvml.Err(last);
            }
            int done = 0;
            if (Hw != null)
                foreach (LHM.ISensor s in Hw.Sensors)
                    if (s.SensorType.ToString() == "Control" && s.Control != null)
                    {
                        try { s.Control.SetSoftware(pct); done++; } catch (Exception) { }
                    }
            if (done > 0) { msg = "OK (LibreHardwareMonitor): " + done + " canal(is) em " + pct + "%"; return true; }
            if (Oc != null && Oc.CanFan)
            {
                string r = Oc.SetFan(pct);
                if (GpuOc.IsOk(r)) { msg = r; return true; }
                nvErr = (nvErr != null ? nvErr + " | " : "") + r;
            }
            msg = "Falhou" + (nvErr != null ? " - " + nvErr : "") + ". Esta placa/driver nao aceitou controle manual.";
            return false;
        }

        // Offset de clock (MHz). Tenta NVML antigo -> NVML novo -> NvAPI. Retorna resumo.
        public string SetClockOffset(bool core, int mhz)
        {
            List<string> errs = new List<string>();
            int drv = core ? mhz : mhz * MemFactor;
            int r = core ? Nvml.SetGpcOffset(H, drv) : Nvml.SetMemOffset(H, drv);
            if (r == 0) return "OK (NVML)";
            errs.Add("NVML: " + Nvml.Err(r));
            r = Nvml.SetOffsetNew(H, core ? 0 : 2, drv);
            if (r == 0) return "OK (NVML novo)";
            errs.Add("NVML2: " + Nvml.Err(r));
            string e = NvApi.SetOffset(NvIndex, core ? 0 : 4, drv * 1000);
            if (e == null) return "OK (NvAPI)";
            errs.Add(e);
            return "FALHOU - " + string.Join(" | ", errs.ToArray());
        }

        public bool SetFanAuto(out string msg)
        {
            bool ok = false;
            if (Nv)
            {
                int n = Math.Max(1, FanCount());
                ok = true;
                for (int f = 0; f < n; f++)
                {
                    int r1 = Nvml.SetFanPolicy(H, (uint)f, 0);
                    int r2 = Nvml.SetFanDefault(H, (uint)f);
                    if (r1 != 0 && r2 != 0) ok = false;
                }
            }
            if (!ok && Hw != null)
                foreach (LHM.ISensor s in Hw.Sensors)
                    if (s.SensorType.ToString() == "Control" && s.Control != null)
                    {
                        try { s.Control.SetDefault(); ok = true; } catch (Exception) { }
                    }
            if (!ok && Oc != null && Oc.CanFan) ok = GpuOc.IsOk(Oc.FanAuto());
            msg = ok ? "Controle automatico (curva original da placa) restaurado." : "Nao foi possivel restaurar o modo automatico.";
            return ok;
        }

        // Sensores extras lidos direto do NVML (somam com os do LibreHardwareMonitor)
        static Sn Mk(string g, string n, string t, Func<float?> f) { Sn s = new Sn(); s.Group = g; s.Name = n; s.Type = t; s.Get = f; return s; }
        public List<Sn> Virtual()
        {
            List<Sn> l = new List<Sn>();
            if (!Nv) return l;
            IntPtr h = H;
            string g = "NVML - " + Name;
            l.Add(Mk(g, "Temperatura do nucleo", "Temperature", delegate { return F(Nvml.Temp(h)); }));
            l.Add(Mk(g, "Uso do nucleo", "Load", delegate { uint a, b; return Nvml.GetUtil(h, out a, out b) ? (float?)a : null; }));
            l.Add(Mk(g, "Uso do controlador de memoria", "Load", delegate { uint a, b; return Nvml.GetUtil(h, out a, out b) ? (float?)b : null; }));
            l.Add(Mk(g, "VRAM usada", "SmallData", delegate { ulong t, u; return Nvml.Mem(h, out t, out u) ? (float?)(u / 1048576.0) : null; }));
            l.Add(Mk(g, "VRAM total", "SmallData", delegate { ulong t, u; return Nvml.Mem(h, out t, out u) ? (float?)(t / 1048576.0) : null; }));
            l.Add(Mk(g, "VRAM em uso (%)", "Load", delegate { ulong t, u; return Nvml.Mem(h, out t, out u) && t > 0 ? (float?)(100.0 * u / t) : null; }));
            l.Add(Mk(g, "Consumo (watts)", "Power", delegate { uint? p = Nvml.Power(h); return p.HasValue ? (float?)(p.Value / 1000f) : null; }));
            l.Add(Mk(g, "Limite de potencia (TDP)", "Power", delegate { uint? p = Nvml.PowerLimit(h); return p.HasValue ? (float?)(p.Value / 1000f) : null; }));
            l.Add(Mk(g, "Ventoinha (%)", "Control", delegate { return F(Nvml.Fan(h, 0)); }));
            l.Add(Mk(g, "Ventoinha (RPM)", "Fan", delegate { return F(Nvml.FanRpm(h, 0)); }));
            l.Add(Mk(g, "Clock do nucleo", "Clock", delegate { return F(Nvml.Clock(h, 0)); }));
            l.Add(Mk(g, "Clock SM", "Clock", delegate { return F(Nvml.Clock(h, 1)); }));
            l.Add(Mk(g, "Clock da memoria", "Clock", delegate { return F(Nvml.Clock(h, 2)); }));
            l.Add(Mk(g, "Clock de video", "Clock", delegate { return F(Nvml.Clock(h, 3)); }));
            l.Add(Mk(g, "PCIe - geracao", "Factor", delegate { return F(Nvml.Pcie(h, false, false)); }));
            l.Add(Mk(g, "PCIe - largura (x)", "Factor", delegate { return F(Nvml.Pcie(h, true, false)); }));
            return l;
        }

        static string Fm(uint? v, string suf) { return v.HasValue ? v.Value + suf : null; }

        public List<string[]> Specs()
        {
            List<string[]> s = new List<string[]>();
            Action<string, string> add = delegate(string k, string v) { if (v != null) s.Add(new string[] { k, v }); };
            add("Nome", Name);
            add("Fabricante", Vendor);
            if (Nv)
            {
                add("Driver NVIDIA", Nvml.Driver());
                add("Arquitetura", Nvml.Arch(H));
                add("Compute capability (CUDA)", Nvml.Compute(H));
                add("Nucleos CUDA", Fm(Nvml.Cores(H), ""));
                add("VBIOS", Nvml.Vbios(H));
                add("UUID", Nvml.Uuid(H));
                ulong t, u;
                if (Nvml.Mem(H, out t, out u)) add("Memoria de video (VRAM)", (t / 1048576UL) + " MB");
                add("Barramento da memoria", Fm(Nvml.MemBus(H), " bits"));
                uint? g1 = Nvml.Pcie(H, false, false), w1 = Nvml.Pcie(H, true, false), g2 = Nvml.Pcie(H, false, true), w2 = Nvml.Pcie(H, true, true);
                if (g1.HasValue && w1.HasValue) add("PCIe (agora)", "Gen " + g1.Value + " x" + w1.Value);
                if (g2.HasValue && w2.HasValue) add("PCIe (maximo da placa)", "Gen " + g2.Value + " x" + w2.Value);
                string[] cn = { "Clock do nucleo", "Clock SM", "Clock da memoria", "Clock de video" };
                for (int i = 0; i < 4; i++)
                {
                    uint? c = Nvml.Clock(H, i), m = Nvml.MaxClock(H, i);
                    if (c.HasValue || m.HasValue) add(cn[i] + " (agora / maximo)", (c.HasValue ? c.Value.ToString() : "?") + " / " + (m.HasValue ? m.Value.ToString() : "?") + " MHz");
                }
                uint? pl = Nvml.PowerLimit(H), pd = Nvml.PowerDefault(H);
                uint pmn, pmx;
                if (pl.HasValue) add("Limite de potencia atual", (pl.Value / 1000f).ToString("0") + " W");
                if (pd.HasValue) add("Limite de potencia padrao", (pd.Value / 1000f).ToString("0") + " W");
                if (Nvml.PowerRange(H, out pmn, out pmx)) add("Faixa de potencia permitida", (pmn / 1000f).ToString("0") + Lang.Pick(" a ", " to ") + (pmx / 1000f).ToString("0") + " W");
                add("Temp. de desligamento", Fm(Nvml.TempThr(H, 0), " \u00B0C"));
                add("Temp. de slowdown (throttle)", Fm(Nvml.TempThr(H, 1), " \u00B0C"));
                add("Temp. maxima da GPU", Fm(Nvml.TempThr(H, 2), " \u00B0C"));
                add("Temp. maxima da memoria", Fm(Nvml.TempThr(H, 3), " \u00B0C"));
                add("Temp. alvo (limite de temperatura)", Fm(Nvml.TempThr(H, 5), " \u00B0C"));
                add("Quantidade de ventoinhas", FanCount().ToString());
                uint fmn, fmx;
                if (Nvml.MinMaxFan(H, out fmn, out fmx)) add("Faixa da ventoinha (driver)", fmn + Lang.Pick("% a ", "% to ") + fmx + "%");
                int? ps = Nvml.Pstate(H);
                if (ps.HasValue) add("Estado de desempenho", "P" + ps.Value);
            }
            else
            {
                add("Leitura de sensores", "LibreHardwareMonitor" + (Oc != null ? " + " + Oc.Backend : ""));
                long vram; string drv, drvDate;
                GpuReg.Read(Name, out vram, out drv, out drvDate);
                if (vram > 0) add("Memoria de video (VRAM)", (vram / 1048576L) + " MB");
                add("Driver (registro)", drv);
                add("Data do driver (registro)", drvDate);
                string[] live = { "Temperature|Temperatura", "Temperature|Hot Spot|Temperatura hotspot", "Clock|Core|Clock do nucleo (agora)", "Clock|Memory|Clock da memoria (agora)",
                                  "Power|Consumo (agora)", "Load|Core|Uso do nucleo (agora)", "Fan|Ventoinha (RPM agora)", "Control|Ventoinha (% agora)" };
                foreach (string lv in live)
                {
                    string[] p = lv.Split('|');
                    float? val = p.Length == 3 ? Lhm(p[0], p[1]) : Lhm(p[0]);
                    string label = p[p.Length - 1];
                    if (val.HasValue) add(label, val.Value.ToString("0.#") + (p[0] == "Temperature" ? " \u00B0C" : p[0] == "Clock" ? " MHz" : p[0] == "Power" ? " W" : p[0] == "Load" || p[0] == "Control" ? " %" : ""));
                }
                if (Oc != null)
                {
                    add("--- Overclock / controle (" + Oc.Backend + ") ---", " ");
                    Oc.Refresh();
                    Action<string, OcCap> cap = delegate(string k, OcCap c)
                    {
                        if (c.Ok) add(k, "Ligado - faixa " + c.Min + Lang.Pick(" a ", " to ") + c.Max + " " + c.Unit);
                        else add(k, "Indisponivel (" + c.Why + ")");
                    };
                    cap("Offset do clock do nucleo", Oc.Core); cap("Offset do clock da memoria", Oc.Mem);
                    cap("Limite de potencia", Oc.Power); cap("Limite de temperatura", Oc.Temp);
                    add("Controle da ventoinha", Oc.CanFan ? "Ligado" : "Indisponivel (placa/driver nao expoe)");
                    if (Oc.Note.Length > 0) add("Observacao", Oc.Note);
                }
                else add("Overclock / ventoinha", "Indisponivel (" + (OcWhy.Length > 0 ? OcWhy : "sem backend para este fabricante") + ")");
            }
            if (Hw != null)
            {
                add("Tipo (LHM)", Hw.HardwareType.ToString());
                float? vm = Lhm("SmallData", "Memory Total");
                if (vm.HasValue && !Nv) add("Memoria de video (VRAM)", vm.Value.ToString("0") + " MB");
                add("Sensores expostos (LHM)", Hw.Sensors.Length.ToString());
            }

            add("--- Recursos (ligado / desligado) ---", " ");
            ulong bt, bu;
            if (Nv && Nvml.Bar1(H, out bt, out bu))
            {
                bool big = bt > 268435456UL;
                add("Resizable BAR (Re-Size BAR)", (big ? "Ligado" : "Desligado") + " (BAR1 = " + (bt / 1048576UL) + " MB)");
                add("Above 4G Decoding", big ? "Ligado (exigido pelo BAR grande)" : "Desligado ou nao confirmado (BAR1 de 256 MB)");
            }
            else add("Resizable BAR / Above 4G Decoding", Nv ? "Indisponivel" : "Indisponivel (a leitura exata so existe no driver NVIDIA; veja no AMD Adrenalin: Smart Access Memory, ou na BIOS)");
            add("Boot em modo UEFI (necessario p/ ReBAR)", Platform.UefiText());
            add("Aceleracao de GPU por hardware (HAGS)", Platform.Hags());
            add("Modo de jogo (Game Mode)", Platform.GameMode());
            if (Nv)
            {
                int? da = Nvml.DisplayActive(H);
                if (da.HasValue) add("Monitor conectado / display ativo", da.Value == 1 ? "Ligado" : "Desligado");
                int? ecc = Nvml.EccCurrent(H);
                add("ECC da memoria", ecc.HasValue ? (ecc.Value == 1 ? "Ligado" : "Desligado") : "Indisponivel (placa sem ECC)");
                int? gsp = Nvml.Gsp(H);
                if (gsp.HasValue) add("Firmware GSP", gsp.Value == 1 ? "Ligado" : "Desligado");
                int? cm = Nvml.ComputeMode(H);
                if (cm.HasValue) add("Modo de computacao", cm.Value == 0 ? "Padrao" : (cm.Value == 2 ? "Proibido" : "Exclusivo"));
                add("Limitacao de clock agora", Nvml.Throttle(H));
            }
            return s;
        }
    }

    // ------------------------------------------------------------------
    //  Informacoes estaticas da CPU (WMI + registro + CPUID do Windows)
    // ------------------------------------------------------------------
    static class CpuInfo
    {
        [DllImport("kernel32.dll")] static extern bool IsProcessorFeaturePresent(uint f);

        public static string P(ManagementBaseObject mo, string name)
        {
            try { object o = mo[name]; return o == null ? null : o.ToString().Trim(); } catch (Exception) { return null; }
        }
        static string Arch(string a)
        {
            switch (a)
            {
                case "0": return "x86"; case "5": return "ARM"; case "6": return "Itanium"; case "9": return "x64"; case "12": return "ARM64";
                default: return a;
            }
        }
        static string Kb(string v) { return (string.IsNullOrEmpty(v) || v == "0") ? null : v + " KB"; }
        static string Mhz(string v) { return string.IsNullOrEmpty(v) ? null : v + " MHz"; }
        static string YesNo(string v) { return v == null ? null : (v.ToLower() == "true" ? "sim" : "nao"); }

        public static List<string[]> Collect()
        {
            List<string[]> l = new List<string[]>();
            Action<string, string> add = delegate(string k, string v) { if (!string.IsNullOrEmpty(v)) l.Add(new string[] { k, v }); };
            int coresSum = 0, thrSum = 0;
            try
            {
                using (ManagementObjectSearcher srch = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
                using (ManagementObjectCollection col = srch.Get())
                {
                    int n = col.Count, i = 0;
                    foreach (ManagementBaseObject mo in col)
                    {
                        i++;
                        string pre = n > 1 ? "CPU " + i + " - " : "";
                        add(pre + "Nome", P(mo, "Name"));
                        add(pre + "Fabricante", P(mo, "Manufacturer"));
                        add(pre + "Familia / modelo / stepping", P(mo, "Description"));
                        add(pre + "Arquitetura", Arch(P(mo, "Architecture")));
                        add(pre + "Soquete", P(mo, "SocketDesignation"));
                        add(pre + "Nucleos fisicos", P(mo, "NumberOfCores"));
                        add(pre + "Threads (processadores logicos)", P(mo, "NumberOfLogicalProcessors"));
                        int cv, tv;
                        if (int.TryParse(P(mo, "NumberOfCores"), out cv)) coresSum += cv;
                        if (int.TryParse(P(mo, "NumberOfLogicalProcessors"), out tv)) thrSum += tv;
                        if (i == 1) CpuId.Socket = P(mo, "SocketDesignation") ?? "";
                        add(pre + "Clock maximo", Mhz(P(mo, "MaxClockSpeed")));
                        add(pre + "Clock atual (WMI)", Mhz(P(mo, "CurrentClockSpeed")));
                        add(pre + "Clock externo (BCLK)", Mhz(P(mo, "ExtClock")));
                        add(pre + "Cache L2", Kb(P(mo, "L2CacheSize")));
                        add(pre + "Cache L3", Kb(P(mo, "L3CacheSize")));
                        string aw = P(mo, "AddressWidth");
                        add(pre + "Largura de endereco", aw == null ? null : aw + " bits");
                        add(pre + "ID do processador", P(mo, "ProcessorId"));
                        add(pre + "Revisao", P(mo, "Revision"));
                        mo.Dispose();
                    }
                }
            }
            catch (Exception ex) { add("Aviso", "WMI indisponivel: " + ex.Message); }

            if (l.Count <= 1)
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    {
                        if (k != null)
                        {
                            add("Nome", Convert.ToString(k.GetValue("ProcessorNameString")).Trim());
                            add("Identificador", Convert.ToString(k.GetValue("Identifier")));
                            add("Fabricante", Convert.ToString(k.GetValue("VendorIdentifier")));
                            object mhz = k.GetValue("~MHz");
                            if (mhz != null) add("Clock base", mhz + " MHz");
                        }
                    }
                }
                catch (Exception) { }
                add("Threads (processadores logicos)", Environment.ProcessorCount.ToString());
            }

            string[] fn = { "MMX", "SSE", "SSE2", "SSE3", "SSSE3", "SSE4.1", "SSE4.2", "AVX", "AVX2", "AVX-512" };
            uint[] fid = { 3, 6, 10, 13, 36, 37, 38, 39, 40, 41 };
            StringBuilder sb = new StringBuilder();
            try
            {
                for (int i = 0; i < fn.Length; i++)
                    if (IsProcessorFeaturePresent(fid[i])) { if (sb.Length > 0) sb.Append(", "); sb.Append(fn[i]); }
                add("Conjuntos de instrucoes", sb.ToString());
            }
            catch (Exception) { }

            CpuId.Cores = coresSum; if (thrSum > 0) CpuId.Threads = thrSum;
            add("--- Recursos e seguranca (ligado / desligado) ---", " ");
            if (thrSum > 0 && coresSum > 0)
                add("Hyper-Threading / SMT", thrSum > coresSum ? "Ligado (" + coresSum + " nucleos, " + thrSum + " threads)" : "Desligado (1 thread por nucleo)");
            l.AddRange(Platform.Fast());
            l.AddRange(Platform.Slow());
            return l;
        }
    }

    // ------------------------------------------------------------------
    //  Central de sensores
    // ------------------------------------------------------------------
    // ------------------------------------------------------------------
    //  Zonas termicas do Windows/ACPI (alternativa quando o driver nao le a CPU)
    // ------------------------------------------------------------------
    static class AcpiTemp
    {
        static Dictionary<string, float> vals = new Dictionary<string, float>();
        static object lk = new object();
        public static void Refresh()
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\CIMV2", "SELECT * FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation"))
                using (ManagementObjectCollection col = s.Get())
                    foreach (ManagementBaseObject mo in col)
                    {
                        string n = CpuInfo.P(mo, "Name"), hp = CpuInfo.P(mo, "HighPrecisionTemperature"), t = CpuInfo.P(mo, "Temperature");
                        double k, c = double.NaN;
                        if (hp != null && double.TryParse(hp, NumberStyles.Float, CultureInfo.InvariantCulture, out k) && k > 0) c = k / 10.0 - 273.15;
                        else if (t != null && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out k) && k > 0) c = k - 273.15;
                        if (n != null && !double.IsNaN(c) && c > 0 && c < 150) lock (lk) vals[n] = (float)c;
                        mo.Dispose();
                    }
            }
            catch (Exception) { }
            try
            {
                using (ManagementObjectSearcher s2 = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature"))
                using (ManagementObjectCollection col2 = s2.Get())
                    foreach (ManagementBaseObject mo in col2)
                    {
                        string n = CpuInfo.P(mo, "InstanceName"), t = CpuInfo.P(mo, "CurrentTemperature");
                        double k;
                        if (n != null && t != null && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out k) && k > 0)
                        {
                            double c = k / 10.0 - 273.15;
                            if (c > 0 && c < 150) lock (lk) vals["ACPI " + n] = (float)c;
                        }
                        mo.Dispose();
                    }
            }
            catch (Exception) { }
        }
        public static float? Get(string n) { lock (lk) { float v; return vals.TryGetValue(n, out v) ? (float?)v : null; } }
        public static List<string> Names() { lock (lk) return new List<string>(vals.Keys); }
    }

    static class SensorHub
    {
        static LHM.Computer pc;
        static List<Sn> all = new List<Sn>();
        public static List<Gpu> Gpus = new List<Gpu>();
        public static List<Sn> GpuSensors = new List<Sn>();
        public static List<Sn> PcSensors = new List<Sn>();
        public static string Error;

        static List<LHM.IHardware> hwList = new List<LHM.IHardware>();
        static System.Threading.Thread worker;
        static volatile bool running;
        static int tickNo;
        public static volatile string AlertText;
        public static volatile int IntervalMs = 1000;
        static DateTime lastSlow = DateTime.MinValue;
        public static void SetInterval(int ms)
        {
            IntervalMs = Math.Max(100, Math.Min(10000, ms));
            Settings.Set("interval", IntervalMs.ToString());
        }
        static void LoadInterval()
        {
            int v;
            if (int.TryParse(Settings.Get("interval", "1000"), out v)) IntervalMs = Math.Max(100, Math.Min(10000, v));
        }

        public static Sn Pick(string groupPrefix, string type, params string[] names)
        {
            foreach (string nm in names)
            {
                foreach (Sn s in PcSensors)
                    if (s.Type == type && (type != "Temperature" || s.IsRealTemp) && s.Group.StartsWith(groupPrefix) && s.Seen && string.Equals(s.Name, nm, StringComparison.OrdinalIgnoreCase)) return s;
                foreach (Sn s in PcSensors)
                    if (s.Type == type && (type != "Temperature" || s.IsRealTemp) && s.Group.StartsWith(groupPrefix) && s.Seen && s.Name.IndexOf(nm, StringComparison.OrdinalIgnoreCase) >= 0) return s;
            }
            return null;
        }
        static string GuessVendor(string n)
        {
            string u = n.ToUpper();
            if (u.Contains("NVIDIA") || u.Contains("GEFORCE") || u.Contains("QUADRO")) return "NVIDIA";
            if (u.Contains("AMD") || u.Contains("RADEON") || u.Contains("ATI ")) return "AMD";
            if (u.Contains("INTEL")) return "Intel";
            if (u.Contains("MOORE") || u.Contains("MTT")) return "Moore Threads";
            return "Outro";
        }
        // true se o driver de sensores esta lendo temperatura real da CPU (e nao so a zona termica do Windows)
        public static bool CpuDriverOk()
        {
            foreach (Sn s in PcSensors)
                if (s.Group.StartsWith("Processador") && !s.Group.Contains("Zona termica") && s.IsRealTemp && s.Seen) return true;
            return false;
        }
        public static float? CpuTemp()
        {
            Sn s = Pick("Processador", "Temperature", "CPU Package", "Package", "Tdie", "CCD", "Core Max", "Core Average", "Tctl", "Core", "");
            return s != null ? s.Last : null;
        }


        static string Kind(string t)
        {
            switch (t)
            {
                case "Cpu": return "Processador";
                case "Motherboard": return "Placa-mae";
                case "SuperIO": return "Placa-mae (Super I/O)";
                case "Memory": return "Memoria RAM";
                case "Storage": return "Armazenamento (HD/SSD)";
                case "Cooler": return "Cooler / Water cooler";
                case "Network": return "Rede";
                case "Controller": return "Controladora";
                case "Battery": return "Bateria";
                case "Psu": return "Fonte";
                case "EmbeddedController": return "Controlador embarcado";
                default: return t;
            }
        }

        static bool SameName(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0 || b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string FallbackGpuName(Gpu g)
        {
            try
            {
                using (ManagementObjectSearcher vs = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                using (ManagementObjectCollection vc = vs.Get())
                    foreach (ManagementBaseObject vm in vc)
                    {
                        string vn = CpuInfo.P(vm, "Name");
                        vm.Dispose();
                        if (!string.IsNullOrEmpty(vn) && GuessVendor(vn) == g.Vendor) return vn;
                    }
            }
            catch (Exception) { }
            return g.Vendor + " GPU";
        }

        static void Collect(LHM.IHardware h, string group, List<Sn> into)
        {
            foreach (LHM.ISensor s in h.Sensors)
            {
                LHM.ISensor sc = s;
                Sn n = new Sn();
                n.Group = group; n.Name = s.Name; n.Type = s.SensorType.ToString(); n.Ls = s;
                n.Get = delegate { return sc.Value; };
                into.Add(n);
            }
            foreach (LHM.IHardware sub in h.SubHardware) Collect(sub, group + " > " + sub.Name, into);
        }

        public static void Open()
        {
            LoadInterval();
            try
            {
                pc = new LHM.Computer();
                // liga cada tipo de hardware por reflexao: funciona mesmo que sua versao do LibreHardwareMonitor nao tenha alguma propriedade
                foreach (string pn in new string[] { "IsCpuEnabled", "IsGpuEnabled", "IsMemoryEnabled", "IsMotherboardEnabled", "IsControllerEnabled" })
                {
                    try { PropertyInfo pi = pc.GetType().GetProperty(pn); if (pi != null && pi.CanWrite) pi.SetValue(pc, true, null); } catch (Exception) { }
                }
                Startup.Mark("LHM configurado");
                pc.Open();
                Startup.Mark("LHM aberto (CPU, GPU, placa-mae, memoria)");
                hwList = new List<LHM.IHardware>(pc.Hardware);
            }
            catch (Exception ex) { Error = ex.Message; pc = null; }

            Nvml.Init();
            Startup.Mark("NVML iniciado");
            List<IntPtr> devs = Nvml.Devices();
            bool[] used = new bool[devs.Count];

            if (pc != null)
            {
                foreach (LHM.IHardware h in pc.Hardware)
                {
                    string t = h.HardwareType.ToString();
                    if (t.StartsWith("Gpu"))
                    {
                        Gpu g = new Gpu();
                        g.Hw = h; g.Name = h.Name;
                        g.Vendor = t == "GpuNvidia" ? "NVIDIA" : t == "GpuAmd" ? "AMD" : t == "GpuIntel" ? "Intel" : t;
                        if (t == "GpuNvidia")
                        {
                            for (int i = 0; i < devs.Count && !g.Nv; i++)
                                if (!used[i] && SameName(Nvml.Name(devs[i]), h.Name)) { used[i] = true; g.H = devs[i]; g.Nv = true; g.NvIndex = i; }
                            for (int i = 0; i < devs.Count && !g.Nv; i++)
                                if (!used[i]) { used[i] = true; g.H = devs[i]; g.Nv = true; g.NvIndex = i; }
                        }
                        Gpus.Add(g);
                        Collect(h, g.Name, GpuSensors);
                    }
                    else Collect(h, Kind(t) + " - " + h.Name, PcSensors);
                }
            }
            for (int i = 0; i < devs.Count; i++)
                if (!used[i])
                {
                    Gpu g = new Gpu();
                    g.Nv = true; g.H = devs[i]; g.NvIndex = i; g.Vendor = "NVIDIA"; g.Name = Nvml.Name(devs[i]) ?? "NVIDIA GPU";
                    Gpus.Add(g);
                }
            if (Gpus.Count == 0)
            {
                // nenhuma GPU achada pelo LibreHardwareMonitor/NVML (placas antigas, marcas menos comuns...): usa o WMI do Windows
                try
                {
                    using (ManagementObjectSearcher vs = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                    using (ManagementObjectCollection vc = vs.Get())
                        foreach (ManagementBaseObject vm in vc)
                        {
                            string vn = CpuInfo.P(vm, "Name");
                            if (!string.IsNullOrEmpty(vn) && vn.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) < 0 && vn.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                Gpu ng = new Gpu(); ng.Name = vn; ng.Vendor = GuessVendor(vn); Gpus.Add(ng);
                            }
                            vm.Dispose();
                        }
                }
                catch (Exception) { }
            }
            foreach (Gpu g in Gpus) if (string.IsNullOrEmpty(g.Name) || g.Name.Trim().Length == 0) g.Name = FallbackGpuName(g);
            Startup.Mark("GPUs listadas");
            foreach (Gpu g in Gpus) g.AttachOc();
            Startup.Mark("OC das GPUs (NVML/ADL/IGCL)");
            foreach (Gpu g in Gpus) GpuSensors.AddRange(g.Virtual());

            foreach (Sn gs in GpuSensors) gs.IsGpu = true;
            all.AddRange(GpuSensors);
            all.AddRange(PcSensors);
            LoadAlerts();
            running = true;
            worker = new System.Threading.Thread(Loop);
            worker.IsBackground = true;
            worker.Name = "BunoSensors";
            worker.Start();
            Startup.Mark("sensores prontos (fase 1)");
            Th.Thread p2 = new Th.Thread(Phase2);
            p2.IsBackground = true; p2.Name = "BunoSensors2"; p2.Start();
        }

        // Fase 2 (em segundo plano): discos (SMART), rede, bateria, fonte e zonas ACPI -> a janela abre sem esperar por eles
        static void Phase2()
        {
            try
            {
                foreach (string pn in new string[] { "IsStorageEnabled", "IsNetworkEnabled", "IsBatteryEnabled", "IsPsuEnabled" })
                {
                    try { PropertyInfo pi = pc.GetType().GetProperty(pn); if (pi != null && pi.CanWrite) pi.SetValue(pc, true, null); } catch (Exception) { }
                    Startup.Mark("fase 2: " + pn);
                }
                List<Sn> add = new List<Sn>();
                List<LHM.IHardware> nh = new List<LHM.IHardware>(hwList);
                foreach (LHM.IHardware h in pc.Hardware)
                {
                    if (hwList.Contains(h)) continue;
                    nh.Add(h);
                    Collect(h, Kind(h.HardwareType.ToString()) + " - " + h.Name, add);
                }
                AcpiTemp.Refresh();
                foreach (string zn in AcpiTemp.Names())
                {
                    Sn z = new Sn(); string nn = zn;
                    z.Group = "Processador - Zona termica (Windows/ACPI)"; z.Name = "Zona " + zn; z.Type = "Temperature";
                    z.Get = delegate { return AcpiTemp.Get(nn); };
                    add.Add(z);
                }
                // copia e troca: quem esta lendo as listas nunca ve uma lista sendo alterada
                List<Sn> np = new List<Sn>(PcSensors); np.AddRange(add);
                List<Sn> na = new List<Sn>(all); na.AddRange(add);
                foreach (Sn x in add) LoadAlertFor(x);
                hwList = nh; all = na; PcSensors = np;
                Startup.Mark("fase 2 concluida (+" + add.Count + " sensores)");
            }
            catch (Exception ex) { Startup.Mark("fase 2 erro: " + ex.Message); }
        }

        // Toda a leitura pesada (LibreHardwareMonitor) roda aqui, fora da tela -> sem travadas
        static void Loop()
        {
            while (running)
            {
                DateTime t0 = DateTime.UtcNow;
                try { UpdateOnce(); } catch (Exception) { }
                tickNo++;
                int wait = IntervalMs - (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                System.Threading.Thread.Sleep(wait < 20 ? 20 : wait);
            }
        }
        static void UpdateRec(LHM.IHardware h)
        {
            h.Update();
            foreach (LHM.IHardware sub in h.SubHardware) UpdateRec(sub);
        }
        static void UpdateOnce()
        {
            bool slow = (DateTime.UtcNow - lastSlow).TotalSeconds >= 5;   // discos (SMART) e zonas ACPI so a cada 5 s
            if (slow) { lastSlow = DateTime.UtcNow; AcpiTemp.Refresh(); }
            foreach (LHM.IHardware h in hwList)
            {
                if (!running) return;
                string t = h.HardwareType.ToString();
                if (!slow && (t == "Storage" || t == "Network")) continue;
                try { UpdateRec(h); } catch (Exception) { }
            }
            foreach (Sn s in all) s.Poll();

            // alertas (tocam som a cada 15 s enquanto o limite estiver estourado)
            DateTime now = DateTime.UtcNow;
            string txt = null;
            foreach (Sn s in all)
            {
                if (!s.AlertAbove.HasValue || !s.Last.HasValue || s.Last.Value <= s.AlertAbove.Value) continue;
                txt = "ALERTA: " + s.Name + " = " + s.Fmt(s.Last) + " (limite " + s.AlertAbove.Value.ToString("0.##") + ")";
                if ((now - s.LastBeep).TotalSeconds > 15)
                {
                    s.LastBeep = now;
                    try { System.Media.SystemSounds.Exclamation.Play(); } catch (Exception) { }
                }
            }
            AlertText = txt;
        }

        static void LoadAlerts()
        {
            string v = Settings.Get("alerts", "");
            if (v.Length == 0) return;
            foreach (string e in v.Split(new string[] { "~|~" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = e.Split(new string[] { "~=~" }, StringSplitOptions.None);
                float f;
                if (p.Length != 2 || !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out f)) continue;
                foreach (Sn s in all) if (s.Key == p[0]) { s.AlertAbove = f; break; }
            }
        }

        static void LoadAlertFor(Sn x)
        {
            string v = Settings.Get("alerts", "");
            if (v.Length == 0) return;
            foreach (string e in v.Split(new string[] { "~|~" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = e.Split(new string[] { "~=~" }, StringSplitOptions.None);
                float f;
                if (p.Length == 2 && p[0] == x.Key && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out f)) { x.AlertAbove = f; return; }
            }
        }

        public static void SetAlert(Sn s, float? v)
        {
            s.AlertAbove = v;
            List<string> l = new List<string>();
            foreach (Sn x in all)
                if (x.AlertAbove.HasValue) l.Add(x.Key + "~=~" + x.AlertAbove.Value.ToString("0.####", CultureInfo.InvariantCulture));
            Settings.Set("alerts", string.Join("~|~", l.ToArray()));
        }

        public static string Status()
        {
            bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            return "Administrador: " + (admin ? "sim" : "NAO (rode como administrador)") +
                   "   |   NVML: " + (Nvml.Available ? "ok" : "indisponivel") +
                   "   |   LibreHardwareMonitor: " + (pc != null ? "ok" : "erro - " + Error);
        }

        public static void Close()
        {
            running = false;
            try { if (worker != null) worker.Join(2500); } catch (Exception) { }
            Nvml.Shutdown();
            if (pc != null) { try { pc.Close(); } catch (Exception) { } }
        }
    }

    // ------------------------------------------------------------------
    //  Configuracoes salvas ao lado do .exe (BunoTunner.ini) - portable
    // ------------------------------------------------------------------
    static class Settings
    {
        static Dictionary<string, string> d;
        static string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BunoTunner.ini");
        static void Load()
        {
            d = new Dictionary<string, string>();
            try
            {
                string old = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BunosTunner.ini");
                if (!File.Exists(path) && File.Exists(old)) File.Copy(old, path);   // migra as configuracoes do nome antigo
                if (File.Exists(path))
                    foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
                    }
            }
            catch (Exception) { }
        }
        public static string Get(string k, string def)
        {
            if (d == null) Load();
            string v;
            return d.TryGetValue(k, out v) ? v : def;
        }
        public static void Set(string k, string v)
        {
            if (d == null) Load();
            d[k] = v;
            try
            {
                List<string> l = new List<string>();
                foreach (KeyValuePair<string, string> p in d) l.Add(p.Key + "=" + p.Value);
                File.WriteAllLines(path, l.ToArray(), Encoding.UTF8);
            }
            catch (Exception) { }
        }
    }

    // ------------------------------------------------------------------
    //  Caixa de dialogo simples (pede um texto/valor)
    // ------------------------------------------------------------------
    class PromptForm : XpForm
    {
        XpText tx = new XpText();
        public string Result;
        public PromptForm(string title, string msg, string def)
        {
            Text = title; Size = new Size(400, 170); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            Label l = new Label(); l.BackColor = Color.Transparent; l.Font = Xp.Ui; l.SetBounds(14, 40, 372, 40); l.Text = msg;
            Controls.Add(l);
            tx.SetBounds(14, 86, 372, tx.Height); tx.Text = def; Controls.Add(tx);
            UI.Btn(this, "OK", 220, 118, 80, delegate { Commit(); });
            UI.Btn(this, "Cancelar", 306, 118, 80, delegate { Close(); });
            tx.Box.KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Commit(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
            };
            Shown += delegate { tx.Box.Focus(); tx.Box.SelectAll(); };
        }
        void Commit() { Result = tx.Text.Trim(); DialogResult = DialogResult.OK; Close(); }
        public static string Ask(IWin32Window owner, string title, string msg, string def)
        {
            using (PromptForm f = new PromptForm(title, msg, def))
                return f.ShowDialog(owner) == DialogResult.OK ? f.Result : null;
        }
    }

    // ------------------------------------------------------------------
    //  Janela de sensores (GPU e resto do PC): cartoes, filtros, grafico, alertas, exportar
    // ------------------------------------------------------------------
    class TileDef
    {
        public string Title, Group, Type; public string[] Names;
        public TileDef(string title, string group, string type, params string[] names) { Title = title; Group = group; Type = type; Names = names; }
    }

    class SnSorter : System.Collections.IComparer
    {
        int col; bool asc;
        public SnSorter(int c, bool a) { col = c; asc = a; }
        static float N(float? v) { return v.HasValue ? v.Value : float.MinValue; }
        public int Compare(object x, object y)
        {
            Sn p = (Sn)((ListViewItem)x).Tag, q = (Sn)((ListViewItem)y).Tag;
            int r;
            switch (col)
            {
                case 1: r = N(p.Last).CompareTo(N(q.Last)); break;
                case 2: r = p.Min.CompareTo(q.Min); break;
                case 3: r = p.Max.CompareTo(q.Max); break;
                case 4: r = string.Compare(p.Category, q.Category, StringComparison.OrdinalIgnoreCase); break;
                default: r = string.Compare(p.Name, q.Name, StringComparison.OrdinalIgnoreCase); break;
            }
            return asc ? r : -r;
        }
    }

    class SensorForm : XpForm
    {
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        static readonly string[] Cats = { "Temperatura", "Uso (%)", "Clock", "Potencia", "Voltagem", "Corrente", "Ventoinha", "Controle (%)", "Memoria/Dados", "Taxa de dados", "Outros" };
        static readonly int[] Wins = { 60, 120, 300, 600, 1800 };
        static readonly Color[] Pal = {
            Color.FromArgb(0,200,0), Color.FromArgb(230,50,50), Color.FromArgb(50,130,255), Color.FromArgb(255,140,0),
            Color.FromArgb(190,70,210), Color.FromArgb(0,180,180), Color.FromArgb(240,60,150), Color.FromArgb(150,150,150),
            Color.FromArgb(120,200,60), Color.FromArgb(200,120,60), Color.FromArgb(90,90,230), Color.FromArgb(0,150,100) };

        string key; List<Sn> list; TileDef[] tiles; Sn[] tileSn; Label[] tileVal;
        XpText txSearch; ComboBox cbType, cbWin; XpOption chkHide, chkOnly, chkNorm;
        ListBox lbCat; Label lblHint; bool hintDriver, catUpdating;
        string[] catNames = { "Todos", "GPU", "CPU", "Placa-mae", "Memoria RAM", "Discos", "Ventoinhas", "Coolers", "Rede", "Outros" };
        DateTime openedAt = DateTime.UtcNow;
        const string DefaultHint = "Dica: marque a caixinha (ou de duplo clique) para ver o sensor no grafico. Botao direito = alerta, copiar... Clique no cabecalho para ordenar.";
        ListView lv = new ListView(); XpGraph graph = new XpGraph(); Timer timer = new Timer();
        Label lblInfo; XpButton btnPause; ContextMenuStrip menu, exportMenu; LTip tip = new LTip();
        Dictionary<Sn, Series> ser = new Dictionary<Sn, Series>();
        Dictionary<Sn, ListViewItem> itemOf = new Dictionary<Sn, ListViewItem>();
        bool loading = true, sortAsc = true;
        int colorIdx, lastSeen = -1, sortCol = -1;
        DateTime infoUntil = DateTime.MinValue;
        XpText txInt; int lastPeriod;

        static TileDef[] AllTiles()
        {
            return new TileDef[] {
                new TileDef("GPU - temperatura", "@GPU", "Temperature", "GPU Core", "Temperatura do nucleo", "Core", "Hot Spot", ""),
                new TileDef("GPU - uso", "@GPU", "Load", "GPU Core", "Uso do nucleo", "Core", ""),
                new TileDef("GPU - consumo", "@GPU", "Power", "GPU Package", "Consumo (watts)", "Package", ""),
                new TileDef("CPU - temperatura", "Processador", "Temperature", "CPU Package", "Package", "Tdie", "CCD", "Core Max", "Core Average", "Tctl", "Core", ""),
                new TileDef("CPU - uso", "Processador", "Load", "CPU Total", ""),
                new TileDef("CPU - consumo", "Processador", "Power", "CPU Package", "Package", ""),
                new TileDef("RAM em uso", "Memoria RAM", "Load", "Memory", ""),
                new TileDef("Ventoinha da CPU", "@PC", "Fan", "CPU Fan", "CPU", "Fan #1", "") };
        }
        static bool Match(Sn s, TileDef d)
        {
            if (s.Type != d.Type) return false;
            if (!(s.Seen || s.Last.HasValue)) return false;
            if (d.Type == "Temperature" && !s.IsRealTemp) return false;
            if (d.Group == "@GPU") return s.IsGpu;
            if (d.Group == "@PC") return !s.IsGpu;
            return d.Group == null || s.Group.StartsWith(d.Group);
        }
        static Sn Find(List<Sn> list, TileDef d)
        {
            foreach (string nm in d.Names)
            {
                foreach (Sn s in list)
                    if (Match(s, d) && string.Equals(s.Name, nm, StringComparison.OrdinalIgnoreCase)) return s;
                foreach (Sn s in list)
                    if (Match(s, d) && s.Name.IndexOf(nm, StringComparison.OrdinalIgnoreCase) >= 0) return s;
            }
            return null;
        }

        public SensorForm(string title, List<Sn> sensors)
        {
            key = "all"; list = sensors; Text = title;
            Size = new Size(1120, Math.Min(700, Screen.PrimaryScreen.WorkingArea.Height - 30));
            tiles = AllTiles();
            tileSn = new Sn[tiles.Length]; tileVal = new Label[tiles.Length];
            tip.InitialDelay = 300; tip.AutoPopDelay = 9000;
            KeyPreview = true;

            Panel right = new Panel(); right.Dock = DockStyle.Fill;
            Panel left = new Panel(); left.Dock = DockStyle.Left; left.Width = 650; left.Padding = new Padding(0, 0, 6, 0);
            Panel bottom = new Panel(); bottom.Dock = DockStyle.Bottom; bottom.Height = 36;
            Panel filters = new Panel(); filters.Dock = DockStyle.Top; filters.Height = 34;
            Panel top = new Panel(); top.Dock = DockStyle.Top; top.Height = 74;
            Controls.Add(right); Controls.Add(left); Controls.Add(bottom); Controls.Add(filters); Controls.Add(top);
            BuildTiles(top); BuildFilters(filters); BuildList(left); BuildGraphSide(right); BuildBottom(bottom); BuildMenus();

            // preferencias salvas
            chkHide.Checked = Settings.Get(key + ".hide", "1") == "1";
            chkNorm.Checked = Settings.Get(key + ".norm", "1") == "1"; graph.Normalize = chkNorm.Checked;
            int wi;
            if (!int.TryParse(Settings.Get(key + ".win", "1"), out wi) || wi < 0 || wi >= Wins.Length) wi = 1;
            cbWin.SelectedIndex = wi; ApplyWindow();
            string saved = Settings.Get(key + ".plot", null);
            if (saved == null) Essentials();
            else
                foreach (string k in saved.Split(new string[] { "~|~" }, StringSplitOptions.RemoveEmptyEntries))
                    foreach (Sn s in list)
                        if (s.Key == k) { Plot(s, true); break; }

            EventHandler rb = delegate { if (!catUpdating) Rebuild(); };
            txSearch.Box.TextChanged += rb; lbCat.SelectedIndexChanged += rb; cbType.SelectedIndexChanged += rb;
            chkHide.CheckedChanged += rb; chkOnly.CheckedChanged += rb;
            cbWin.SelectedIndexChanged += delegate { ApplyWindow(); };
            chkNorm.CheckedChanged += delegate { graph.Normalize = chkNorm.Checked; graph.Invalidate(); };
            lv.ItemChecked += OnChecked;
            lv.ItemActivate += delegate
            {
                foreach (ListViewItem it in lv.SelectedItems) { Sn sn = (Sn)it.Tag; Plot(sn, !ser.ContainsKey(sn)); }
            };
            lv.ColumnClick += delegate(object o, ColumnClickEventArgs e)
            {
                if (sortCol == e.Column) sortAsc = !sortAsc; else { sortCol = e.Column; sortAsc = true; }
                Rebuild();
            };

            KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.F) { txSearch.Box.Focus(); txSearch.Box.SelectAll(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape && txSearch.Box.Focused) { txSearch.Text = ""; e.SuppressKeyPress = true; }
            };
            Rebuild();
            timer.Interval = Math.Max(100, SensorHub.IntervalMs); lastPeriod = SensorHub.IntervalMs;
            timer.Tick += delegate { try { Tick2(); } catch (Exception) { } };
            timer.Start();
            FormClosing += delegate { SaveSettings(); };
            Action langCh = delegate { RefreshCats(); };
            Lang.Changed += langCh;
            FormClosed += delegate { timer.Stop(); Lang.Changed -= langCh; };
            Tick2();
        }

        // ---------- construcao da tela ----------
        void BuildTiles(Panel top)
        {
            XpGroup[] tg = new XpGroup[tiles.Length];
            for (int i = 0; i < tiles.Length; i++)
            {
                XpGroup g = new XpGroup(); g.Title = tiles[i].Title; g.Padding = new Padding(4, 20, 4, 2);
                Label v = new Label(); v.Dock = DockStyle.Fill; v.BackColor = Color.Transparent; v.Font = Xp.Big;
                v.ForeColor = Xp.GroupText; v.TextAlign = ContentAlignment.MiddleCenter; v.Text = "-";
                g.Controls.Add(v); top.Controls.Add(g);
                tg[i] = g; tileVal[i] = v;
            }
            EventHandler place = delegate
            {
                int w = (top.Width - 6 * (tg.Length - 1)) / tg.Length;
                for (int j = 0; j < tg.Length; j++) tg[j].SetBounds(j * (w + 6), 4, w, 66);
            };
            top.Resize += place; place(null, EventArgs.Empty);
        }

        void BuildFilters(Panel f)
        {
            UI.Lbl(f, "Buscar (Ctrl+F):", 4, 10, Xp.UiBold);
            txSearch = UI.Txt(f, 104, 6, 190, "");
            UI.Btn(f, "X", 298, 5, 24, delegate { txSearch.Text = ""; txSearch.Box.Focus(); });
            UI.Lbl(f, "Tipo:", 334, 10, Xp.UiBold);
            cbType = new ComboBox(); cbType.DropDownStyle = ComboBoxStyle.DropDownList; cbType.Font = Xp.Ui;
            cbType.SetBounds(368, 6, 120, 22); f.Controls.Add(cbType);
            cbType.Items.Add("Todos os tipos");
            foreach (string c in Cats) cbType.Items.Add(c);
            cbType.SelectedIndex = 0;
            chkHide = UI.Opt(f, "Ocultar sem leitura", 500, 8, 140, false);
            chkOnly = UI.Opt(f, "So no grafico", 646, 8, 100, false);
            UI.Btn(f, "Essenciais", 756, 5, 84, delegate { Essentials(); });
            UI.Btn(f, "Desmarcar tudo", 846, 5, 110, delegate { ClearPlot(); });
            UI.Btn(f, "?  Como usar", 962, 5, 96, delegate { ShowHelp(); });
            tip.SetToolTip(txSearch, "Digite parte do nome. Varias palavras = todas precisam aparecer (ex.: core temp). Ctrl+A seleciona tudo.");
            tip.SetToolTip(txSearch.Box, "Digite parte do nome. Varias palavras = todas precisam aparecer (ex.: core temp). Ctrl+A seleciona tudo.");
            tip.SetToolTip(cbType, "Filtrar por tipo de medida (temperatura, uso, clock...)");
        }

        void BuildList(Panel left)
        {
            lv.Dock = DockStyle.Fill; lv.View = View.Details; lv.CheckBoxes = true; lv.FullRowSelect = true; lv.GridLines = true;
            lv.BackColor = Xp.EditBg; lv.Font = Xp.Ui; lv.HideSelection = false; lv.MultiSelect = true;
            lv.Columns.Add("Sensor", 200); lv.Columns.Add("Valor", 85); lv.Columns.Add("Min", 70); lv.Columns.Add("Max", 70); lv.Columns.Add("Tipo", 70);
            UI.DoubleBuffer(lv);
            lbCat = new ListBox(); lbCat.Dock = DockStyle.Left; lbCat.Width = 140; lbCat.Font = Xp.Ui; lbCat.IntegralHeight = false;
            lbCat.BorderStyle = BorderStyle.FixedSingle;
            foreach (string c in catNames) lbCat.Items.Add(Lang.T(c));
            lbCat.SelectedIndex = 0;
            lblHint = new Label(); lblHint.Dock = DockStyle.Top; lblHint.Height = 34; lblHint.Font = Xp.Ui; lblHint.ForeColor = Xp.Dim;
            lblHint.Text = DefaultHint;
            lblHint.Click += delegate { if (hintDriver) DriverHelp(); };
            left.Controls.Add(lv); left.Controls.Add(lbCat); left.Controls.Add(lblHint);
            tip.SetToolTip(lbCat, "Escolha o que voce quer ver: GPU, CPU, placa-mae, memoria, discos, ventoinhas...");
        }
        void BuildGraphSide(Panel right)
        {
            Panel gt = new Panel(); gt.Dock = DockStyle.Top; gt.Height = 30;
            UI.Lbl(gt, "Janela:", 4, 9, Xp.UiBold);
            cbWin = new ComboBox(); cbWin.DropDownStyle = ComboBoxStyle.DropDownList; cbWin.Font = Xp.Ui; cbWin.SetBounds(52, 5, 70, 22);
            gt.Controls.Add(cbWin);
            cbWin.Items.AddRange(new object[] { "1 min", "2 min", "5 min", "10 min", "30 min" });
            cbWin.SelectedIndex = 1;
            chkNorm = UI.Opt(gt, "Normalizar escala", 128, 6, 124, false); chkNorm.Checked = true;
            btnPause = UI.Btn(gt, "Pausar", 256, 3, 66, null);
            btnPause.Click += delegate { graph.Paused = !graph.Paused; btnPause.Text = graph.Paused ? "Continuar" : "Pausar"; graph.Invalidate(); };
            UI.Btn(gt, "Limpar historico", 326, 3, 112, delegate { foreach (Series s in graph.Items) s.V.Clear(); graph.Invalidate(); });
            tip.SetToolTip(chkNorm, "Ligado: cada linha usa a propria escala (0-100%). Desligado: mesma escala para todas.");
            tip.SetToolTip(graph, "Passe o mouse sobre o grafico para ver os valores de cada instante");
            graph.Dock = DockStyle.Fill;
            right.Controls.Add(graph); right.Controls.Add(gt);
        }

        void BuildBottom(Panel b)
        {
            XpButton ex = UI.Btn(b, "Exportar...", 4, 6, 100, null);
            ex.Click += delegate { exportMenu.Show(ex, new Point(0, ex.Height)); };
            UI.Btn(b, "Zerar min/max", 112, 6, 110, delegate { foreach (Sn s in list) s.ResetMinMax(); });
            UI.Lbl(b, "Atualizar a cada:", 232, 12, Xp.UiBold);
            txInt = UI.Txt(b, 332, 8, 52, SensorHub.IntervalMs.ToString());
            UI.Lbl(b, "ms", 388, 12);
            UI.Btn(b, "OK", 412, 6, 40, delegate { CommitInterval(); });
            txInt.Box.KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitInterval(); } };
            tip.SetToolTip(txInt, "Tempo entre as leituras dos sensores (100 a 10000 ms). Menos = mais fluido, mas usa mais CPU.");
            lblInfo = UI.Lbl(b, "", 466, 12, Xp.Ui, Xp.Dim);
            XpButton close = UI.Btn(b, "Fechar", 0, 6, 90, delegate { Close(); });
            close.Left = b.Width - 96;
            b.Resize += delegate { close.Left = b.Width - 96; };
        }

        void BuildMenus()
        {
            menu = new ContextMenuStrip(); menu.RenderMode = ToolStripRenderMode.System;
            menu.Items.Add("Mostrar / ocultar no grafico", null, delegate { TogglePlot(); });
            menu.Items.Add("Definir alerta (acima de...)", null, delegate { SetAlertDlg(); });
            menu.Items.Add("Remover alerta", null, delegate { RemoveAlert(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Copiar nome", null, delegate { CopySel(false); });
            menu.Items.Add("Copiar valor", null, delegate { CopySel(true); });
            menu.Items.Add("Zerar min/max deste sensor", null, delegate { foreach (ListViewItem it in lv.SelectedItems) ((Sn)it.Tag).ResetMinMax(); });
            lv.ContextMenuStrip = menu; Lang.Menu(menu);

            exportMenu = new ContextMenuStrip(); exportMenu.RenderMode = ToolStripRenderMode.System;
            exportMenu.Items.Add("Valores atuais (CSV)...", null, delegate { ExportValues(); });
            exportMenu.Items.Add("Historico do grafico (CSV)...", null, delegate { ExportHistory(); });
            exportMenu.Items.Add("Imagem do grafico (PNG)...", null, delegate { ExportImage(); });
            Lang.Menu(exportMenu);
        }

        // ---------- logica ----------
        void RefreshCats()
        {
            int[] n = new int[catNames.Length];
            foreach (Sn s in list)
            {
                if (chkHide.Checked && !s.Seen) continue;
                n[0]++;
                for (int i = 1; i < catNames.Length; i++) if (s.InArea(catNames[i])) n[i]++;
            }
            catUpdating = true;
            int sel = lbCat.SelectedIndex;
            for (int i = 0; i < catNames.Length; i++)
            {
                string t = Lang.T(catNames[i]) + "  (" + n[i] + ")";
                if ((string)lbCat.Items[i] != t) lbCat.Items[i] = t;
            }
            if (lbCat.SelectedIndex != sel) lbCat.SelectedIndex = sel;
            catUpdating = false;
        }

        void ShowHelp()
        {
            Dlg.Show(
                "COMO USAR OS SENSORES\n\n" +
                "- Cartoes no topo: os valores principais (GPU, CPU, RAM, ventoinha) sempre a vista.\n" +
                "- Lista: escolha a categoria a esquerda (GPU, CPU, Placa-mae, Memoria...) e use a busca (Ctrl+F) e o filtro de tipo.\n" +
                "- Marque a caixinha (ou de duplo clique) para ver o sensor no grafico.\n" +
                "- Botao direito: criar ALERTA (som), copiar nome/valor, zerar min/max.\n" +
                "- \"Essenciais\" marca os sensores mais importantes de uma vez.\n" +
                "- Grafico: passe o mouse para ver os valores; \"Normalizar escala\" coloca unidades diferentes lado a lado.\n" +
                "- \"Atualizar a cada\" muda a velocidade das leituras (em milissegundos).\n" +
                "- Exportar salva os valores/historico em CSV ou o grafico em PNG.",
                "BunoTunner - Sensores", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void DriverHelp()
        {
            if (Dlg.Show(
                "Os sensores da CPU (temperatura, tensao, consumo) precisam do driver do LibreHardwareMonitor, e ele nao esta conseguindo ler esta CPU.\n\n" +
                "Causas comuns:\n" +
                "1) \"Integridade de memoria\" (Isolamento de nucleo) do Windows ou o Defender bloqueando o driver: desative em Seguranca do Windows > Seguranca do dispositivo > Isolamento de nucleo e reinicie o PC.\n" +
                "2) Versao do LibreHardwareMonitor antiga para a sua CPU: use a mais recente (versoes novas podem exigir o driver PawnIO).\n" +
                "3) Antivirus de terceiros.\n\n" +
                "Enquanto isso o programa usa as zonas termicas do Windows (menos precisas).\n\nAbrir agora a tela de Isolamento de nucleo?",
                "Sensores da CPU sem leitura", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { Process.Start("windowsdefender://coreisolation"); } catch (Exception) { }
        }

        void ApplyWindow()
        {
            int period = Math.Max(100, SensorHub.IntervalMs);
            graph.PeriodMs = period;
            graph.Window = Math.Min(XpGraph.Store, Math.Max(10, Wins[cbWin.SelectedIndex] * 1000 / period));
        }

        void CommitInterval()
        {
            SensorHub.SetInterval(UI.Int(txInt, SensorHub.IntervalMs));
            txInt.Text = SensorHub.IntervalMs.ToString();
            Say("Sensores atualizando a cada " + SensorHub.IntervalMs + " ms.");
        }

        void Say(string msg) { lblInfo.Text = msg; infoUntil = DateTime.UtcNow.AddSeconds(6); }

        void Plot(Sn s, bool on)
        {
            ListViewItem it; itemOf.TryGetValue(s, out it);
            Series se;
            if (on)
            {
                if (ser.ContainsKey(s)) return;
                se = new Series(); se.Sn = s; se.Color = Pal[colorIdx++ % Pal.Length];
                ser[s] = se; graph.Items.Add(se);
                if (it != null) { loading = true; it.Checked = true; it.ForeColor = se.Color; loading = false; }
            }
            else
            {
                if (!ser.TryGetValue(s, out se)) return;
                graph.Items.Remove(se); ser.Remove(s);
                if (it != null) { loading = true; it.Checked = false; it.ForeColor = Xp.Text; loading = false; }
            }
            graph.Invalidate();
        }

        void OnChecked(object o, ItemCheckedEventArgs e)
        {
            if (loading) return;
            Sn sn = (Sn)e.Item.Tag;
            Plot(sn, e.Item.Checked);
            if (chkOnly.Checked && !e.Item.Checked) BeginInvoke(new Action(Rebuild));
        }

        void Essentials()
        {
            foreach (TileDef t in tiles) { Sn s = Find(list, t); if (s != null) Plot(s, true); }
            if (chkOnly != null && chkOnly.Checked) Rebuild();
        }

        void ClearPlot()
        {
            foreach (Sn s in new List<Sn>(ser.Keys)) Plot(s, false);
            if (chkOnly.Checked) Rebuild();
        }

        void Rebuild()
        {
            string q = txSearch.Text.Trim().ToLower();
            string[] words = q.Length == 0 ? new string[0] : q.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string area = (lbCat.SelectedIndex > 0 && lbCat.SelectedIndex < catNames.Length) ? catNames[lbCat.SelectedIndex] : null;
            string cat = cbType.SelectedIndex > 0 && cbType.SelectedIndex <= Cats.Length ? Cats[cbType.SelectedIndex - 1] : null;
            loading = true;
            lv.BeginUpdate();
            lv.Items.Clear(); lv.Groups.Clear(); itemOf.Clear();
            lv.ShowGroups = true;
            Dictionary<string, ListViewGroup> groups = new Dictionary<string, ListViewGroup>();
            int shown = 0;
            foreach (Sn s in list)
            {
                if (area != null && !s.InArea(area)) continue;
                if (cat != null && s.Category != cat) continue;
                if (chkHide.Checked && !s.Seen) continue;
                if (chkOnly.Checked && !ser.ContainsKey(s)) continue;
                if (words.Length > 0)
                {
                    string hay = (s.Name + " " + s.Group).ToLower();
                    bool ok = true;
                    foreach (string w in words) if (hay.IndexOf(w) < 0) { ok = false; break; }
                    if (!ok) continue;
                }
                ListViewItem it = new ListViewItem(new string[] { s.Name, s.Fmt(s.Last), s.Seen ? s.Fmt(s.Min) : "-", s.Seen ? s.Fmt(s.Max) : "-", s.Category });
                it.Tag = s;
                if (true)
                {
                    ListViewGroup g;
                    if (!groups.TryGetValue(s.Group, out g)) { g = new ListViewGroup(s.Group); groups[s.Group] = g; lv.Groups.Add(g); }
                    it.Group = g;
                }
                Series se;
                if (ser.TryGetValue(s, out se)) { it.Checked = true; it.ForeColor = se.Color; }
                lv.Items.Add(it); itemOf[s] = it; shown++;
            }
            if (sortCol >= 0) { lv.ListViewItemSorter = new SnSorter(sortCol, sortAsc); lv.Sort(); }
            else lv.ListViewItemSorter = null;
            lv.EndUpdate();
            loading = false;
            RefreshCats();
            if (lblInfo != null && DateTime.UtcNow > infoUntil) InfoText(shown);
        }

        void InfoText(int shown)
        {
            int alerts = 0;
            foreach (Sn s in list) if (s.AlertAbove.HasValue) alerts++;
            lblInfo.Text = shown + Lang.Pick(" de ", " of ") + list.Count + " sensores  |  " + ser.Count + " no grafico  |  " + alerts + " alerta(s)";
        }

        static void SetSub(ListViewItem it, int i, string t) { t = Lang.T(t); if (it.SubItems[i].Text != t) it.SubItems[i].Text = t; }

        void Tick2()
        {
            if (lastPeriod != SensorHub.IntervalMs)
            {
                lastPeriod = SensorHub.IntervalMs;
                timer.Interval = Math.Max(100, lastPeriod);
                ApplyWindow();
            }
            int seen = 0;
            foreach (Sn s in list) if (s.Seen) seen++;
            if (seen != lastSeen) { lastSeen = seen; if (chkHide.Checked) Rebuild(); else RefreshCats(); }
            bool needHint = !SensorHub.CpuDriverOk() && (DateTime.UtcNow - openedAt).TotalSeconds > 6;
            if (needHint != hintDriver)
            {
                hintDriver = needHint;
                lblHint.ForeColor = needHint ? Xp.Bad : Xp.Dim;
                lblHint.Cursor = needHint ? Cursors.Hand : Cursors.Default;
                lblHint.Text = needHint ? "(!) Os sensores da CPU nao estao sendo lidos pelo driver (temperatura/tensao/consumo). Clique aqui para ver como resolver." : DefaultHint;
            }

            for (int i = 0; i < tiles.Length; i++)
            {
                if (tileSn[i] == null) tileSn[i] = Find(list, tiles[i]);
                Sn t = tileSn[i];
                tileVal[i].Text = (t != null && t.Last.HasValue) ? t.Fmt(t.Last) : "-";
                Color c = Xp.GroupText;
                if (t != null && t.Last.HasValue && t.Type == "Temperature")
                    c = t.Last.Value >= 85 ? Xp.Bad : (t.Last.Value >= 70 ? Color.DarkOrange : Xp.GroupText);
                if (tileVal[i].ForeColor != c) tileVal[i].ForeColor = c;
            }

            lv.BeginUpdate();
            foreach (ListViewItem it in lv.Items)
            {
                Sn s = (Sn)it.Tag;
                SetSub(it, 1, s.Fmt(s.Last));
                SetSub(it, 2, s.Seen ? s.Fmt(s.Min) : "-");
                SetSub(it, 3, s.Seen ? s.Fmt(s.Max) : "-");
                bool al = s.AlertAbove.HasValue;
                bool hit = al && s.Last.HasValue && s.Last.Value > s.AlertAbove.Value;
                string nm = (al ? "(!) " : "") + s.Name;
                if (it.Text != nm) it.Text = nm;
                Color bg = hit ? Xp.HitBg : Xp.EditBg;
                if (it.BackColor != bg) it.BackColor = bg;
            }
            lv.EndUpdate();

            if (!graph.Paused)
                foreach (Series se in graph.Items)
                {
                    if (!se.Sn.Last.HasValue) continue;
                    se.V.Add(se.Sn.Last.Value);
                    if (se.V.Count > XpGraph.Store) se.V.RemoveAt(0);
                }
            graph.Invalidate();
            if (DateTime.UtcNow > infoUntil) InfoText(lv.Items.Count);
        }

        // ---------- menu de contexto ----------
        void TogglePlot()
        {
            List<Sn> sel = new List<Sn>();
            foreach (ListViewItem it in lv.SelectedItems) sel.Add((Sn)it.Tag);
            bool anyOff = false;
            foreach (Sn s in sel) if (!ser.ContainsKey(s)) anyOff = true;
            foreach (Sn s in sel) Plot(s, anyOff);
        }

        void SetAlertDlg()
        {
            if (lv.SelectedItems.Count == 0) return;
            Sn s = (Sn)lv.SelectedItems[0].Tag;
            string cur = s.AlertAbove.HasValue ? s.AlertAbove.Value.ToString("0.##") : "";
            string r = PromptForm.Ask(this, "Alerta", "Avisar (som + barra de status) quando \"" + s.Name + "\" passar de (" + Sn.Unit(s.Type) + "):", cur);
            if (r == null || r.Length == 0) return;
            float v;
            if (!float.TryParse(r.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { Say("Valor invalido."); return; }
            SensorHub.SetAlert(s, v);
            Say("Alerta definido: " + s.Name + " > " + v.ToString("0.##") + " " + Sn.Unit(s.Type));
        }

        void RemoveAlert()
        {
            foreach (ListViewItem it in lv.SelectedItems) SensorHub.SetAlert((Sn)it.Tag, null);
            Say("Alerta removido.");
        }

        void CopySel(bool value)
        {
            StringBuilder sb = new StringBuilder();
            foreach (ListViewItem it in lv.SelectedItems)
            {
                Sn s = (Sn)it.Tag;
                sb.AppendLine(value ? s.Name + ": " + s.Fmt(s.Last) : s.Name);
            }
            if (sb.Length == 0) return;
            try { Clipboard.SetText(sb.ToString()); Say("Copiado."); } catch (Exception) { }
        }

        // ---------- exportar ----------
        static string Csv(string t) { return (t.IndexOf(';') >= 0 || t.IndexOf('"') >= 0) ? "\"" + t.Replace("\"", "\"\"") + "\"" : t; }

        string AskFile(string filter, string name)
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = Lang.T(filter); d.FileName = name + "_" + key + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null;
            }
        }

        void ExportValues()
        {
            string f = AskFile("CSV (*.csv)|*.csv", "sensores");
            if (f == null) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(Lang.T("Grupo;Sensor;Tipo;Valor;Minimo;Maximo;Unidade"));
                foreach (Sn s in list)
                    if (s.Seen)
                        sb.AppendLine(Csv(s.Group) + ";" + Csv(s.Name) + ";" + s.Category + ";" + (s.Last.HasValue ? s.Last.Value.ToString("0.##") : "") + ";" +
                                      s.Min.ToString("0.##") + ";" + s.Max.ToString("0.##") + ";" + Sn.Unit(s.Type));
                File.WriteAllText(f, sb.ToString(), new UTF8Encoding(true));
                Say("Salvo: " + f);
            }
            catch (Exception ex) { Dlg.Show(ex.Message, "Exportar"); }
        }

        void ExportHistory()
        {
            if (graph.Items.Count == 0) { Say("Marque sensores para o grafico primeiro."); return; }
            string f = AskFile("CSV (*.csv)|*.csv", "historico");
            if (f == null) return;
            try
            {
                int max = 0;
                foreach (Series s in graph.Items) max = Math.Max(max, s.V.Count);
                StringBuilder sb = new StringBuilder("Segundos");
                foreach (Series s in graph.Items) sb.Append(";" + Csv(s.Sn.Name + " (" + Sn.Unit(s.Sn.Type) + ")"));
                sb.AppendLine();
                for (int i = 0; i < max; i++)
                {
                    sb.Append(-(max - 1 - i));
                    foreach (Series s in graph.Items)
                    {
                        int idx = s.V.Count - (max - i);
                        sb.Append(";" + (idx >= 0 ? s.V[idx].ToString("0.##") : ""));
                    }
                    sb.AppendLine();
                }
                File.WriteAllText(f, sb.ToString(), new UTF8Encoding(true));
                Say("Salvo: " + f);
            }
            catch (Exception ex) { Dlg.Show(ex.Message, "Exportar"); }
        }

        void ExportImage()
        {
            string f = AskFile("PNG (*.png)|*.png", "grafico");
            if (f == null) return;
            try
            {
                using (Bitmap bmp = new Bitmap(graph.Width, graph.Height))
                {
                    graph.DrawToBitmap(bmp, new Rectangle(0, 0, graph.Width, graph.Height));
                    bmp.Save(f, ImageFormat.Png);
                }
                Say("Salvo: " + f);
            }
            catch (Exception ex) { Dlg.Show(ex.Message, "Exportar"); }
        }

        void SaveSettings()
        {
            List<string> ks = new List<string>();
            foreach (Sn s in ser.Keys) ks.Add(s.Key);
            Settings.Set(key + ".plot", string.Join("~|~", ks.ToArray()));
            Settings.Set(key + ".win", cbWin.SelectedIndex.ToString());
            Settings.Set(key + ".norm", chkNorm.Checked ? "1" : "0");
            Settings.Set(key + ".hide", chkHide.Checked ? "1" : "0");
        }
    }

    // ------------------------------------------------------------------
    //  Recursos do sistema / firmware (ligado ou desligado)
    // ------------------------------------------------------------------
    static class Platform
    {
        [DllImport("kernel32.dll")] static extern bool GetFirmwareType(out int t);
        [DllImport("kernel32.dll")] static extern bool IsProcessorFeaturePresent(uint f);
        public const string On = "Ligado", Off = "Desligado", NA = "Indisponivel";

        static object Reg(RegistryKey root, string sub, string name)
        {
            try { using (RegistryKey k = root.OpenSubKey(sub)) { if (k != null) return k.GetValue(name); } } catch (Exception) { }
            return null;
        }
        static string Flag(object v, int onValue, string naText)
        {
            if (v == null) return naText;
            try { return Convert.ToInt32(v) == onValue ? On : Off; } catch (Exception) { return naText; }
        }
        static string Feature(uint f)
        {
            try { return IsProcessorFeaturePresent(f) ? On : Off; } catch (Exception) { return NA; }
        }

        public static string UefiText()
        {
            try
            {
                int t;
                if (GetFirmwareType(out t)) { if (t == 2) return On + " (UEFI)"; if (t == 1) return Off + " (BIOS legado / CSM)"; }
            }
            catch (Exception) { }
            return NA;
        }
        public static string SecureBoot() { return Flag(Reg(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled"), 1, NA + " (BIOS legado ou sem suporte)"); }
        public static string Hags() { return Flag(Reg(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"), 2, NA + " (Windows/driver sem suporte)"); }
        public static string GameMode()
        {
            object v = Reg(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled");
            return v == null ? "Padrao do Windows (nao alterado)" : Flag(v, 1, NA);
        }
        static string Hvci()
        {
            object v = Reg(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
            return v == null ? Off : Flag(v, 1, NA);
        }
        static string FastStartup() { return Flag(Reg(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled"), 1, NA); }
        static string OsName()
        {
            string k = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            string n = Convert.ToString(Reg(Registry.LocalMachine, k, "ProductName"));
            string dv = Convert.ToString(Reg(Registry.LocalMachine, k, "DisplayVersion"));
            string b = Convert.ToString(Reg(Registry.LocalMachine, k, "CurrentBuild"));
            return (n + " " + dv + " (build " + b + ")").Trim();
        }

        // leituras rapidas (registro / API)
        public static List<string[]> Fast()
        {
            List<string[]> l = new List<string[]>();
            l.Add(new string[] { "Sistema operacional", OsName() });
            l.Add(new string[] { "Boot em modo UEFI", UefiText() });
            l.Add(new string[] { "Secure Boot", SecureBoot() });
            l.Add(new string[] { "Virtualizacao no firmware (VT-x / AMD-V)", Feature(21) });
            l.Add(new string[] { "SLAT (paginacao aninhada)", Feature(20) });
            l.Add(new string[] { "DEP / NX (protecao de execucao)", Feature(12) });
            l.Add(new string[] { "Integridade de memoria (HVCI)", Hvci() });
            l.Add(new string[] { "Inicializacao rapida (Fast Startup)", FastStartup() });
            return l;
        }

        static object Wmi(string scope, string query, string prop)
        {
            try
            {
                ManagementScope sc = new ManagementScope(scope);
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(sc, new ObjectQuery(query)))
                using (ManagementObjectCollection col = s.Get())
                    foreach (ManagementBaseObject mo in col) { object v = mo[prop]; mo.Dispose(); return v; }
            }
            catch (Exception) { }
            return null;
        }

        // leituras via WMI (lentas - rodar em segundo plano)
        public static List<string[]> Slow()
        {
            List<string[]> l = new List<string[]>();
            string tscope = @"\\.\root\CIMV2\Security\MicrosoftTpm";
            object en = Wmi(tscope, "SELECT * FROM Win32_Tpm", "IsEnabled_InitialValue");
            object ver = Wmi(tscope, "SELECT * FROM Win32_Tpm", "SpecVersion");
            string tpm;
            if (en == null) tpm = NA + " (sem TPM ou desativado no firmware)";
            else
            {
                tpm = Convert.ToBoolean(en) ? On : Off;
                if (ver != null) tpm += " (v" + ver.ToString().Split(',')[0].Trim() + ")";
            }
            l.Add(new string[] { "TPM", tpm });

            object vbs = Wmi(@"\\.\root\Microsoft\Windows\DeviceGuard", "SELECT * FROM Win32_DeviceGuard", "VirtualizationBasedSecurityStatus");
            string vb = NA;
            if (vbs != null)
            {
                int n = Convert.ToInt32(vbs);
                vb = n == 2 ? On + " (em execucao)" : (n == 1 ? On + " (aguardando reinicio)" : Off);
            }
            l.Add(new string[] { "Seguranca baseada em virtualizacao (VBS)", vb });

            object hv = Wmi(@"\\.\root\CIMV2", "SELECT * FROM Win32_ComputerSystem", "HypervisorPresent");
            l.Add(new string[] { "Hypervisor ativo (Hyper-V / WSL2)", hv == null ? NA : (Convert.ToBoolean(hv) ? On : Off) });
            return l;
        }
    }

    // ------------------------------------------------------------------
    //  Dados extras da GPU via WMI (driver do Windows, resolucao...)
    // ------------------------------------------------------------------
    static class GpuWmi
    {
        static bool Same(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0 || b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        public static List<string[]> Collect(string gpuName)
        {
            List<string[]> l = new List<string[]>();
            Action<string, string> add = delegate(string k, string v) { if (!string.IsNullOrEmpty(v)) l.Add(new string[] { k, v }); };
            try
            {
                using (ManagementObjectSearcher srch = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                using (ManagementObjectCollection col = srch.Get())
                    foreach (ManagementBaseObject mo in col)
                    {
                        if (!Same(CpuInfo.P(mo, "Name"), gpuName)) { mo.Dispose(); continue; }
                        add("Versao do driver (Windows)", CpuInfo.P(mo, "DriverVersion"));
                        string dd = CpuInfo.P(mo, "DriverDate");
                        if (!string.IsNullOrEmpty(dd))
                        {
                            try { add("Data do driver", ManagementDateTimeConverter.ToDateTime(dd).ToString("dd/MM/yyyy")); } catch (Exception) { }
                        }
                        string hr = CpuInfo.P(mo, "CurrentHorizontalResolution"), vr = CpuInfo.P(mo, "CurrentVerticalResolution");
                        if (hr != null && vr != null) add("Resolucao atual", hr + " x " + vr);
                        string rf = CpuInfo.P(mo, "CurrentRefreshRate");
                        if (rf != null) add("Taxa de atualizacao do monitor", rf + " Hz");
                        add("Processador de video", CpuInfo.P(mo, "VideoProcessor"));
                        add("ID de hardware (PNP)", CpuInfo.P(mo, "PNPDeviceID"));
                        mo.Dispose();
                        break;
                    }
            }
            catch (Exception) { }
            return l;
        }
    }

    // ------------------------------------------------------------------
    //  OpenCL (via OpenCL.dll que ja vem com o driver da GPU) - estresse de GPU
    // ------------------------------------------------------------------
    static class Cl
    {
        const string DLL = "OpenCL.dll";
        [DllImport(DLL)] public static extern int clGetPlatformIDs(uint n, IntPtr[] plats, out uint cnt);
        [DllImport(DLL)] public static extern int clGetDeviceIDs(IntPtr plat, ulong type, uint n, IntPtr[] devs, out uint cnt);
        [DllImport(DLL, EntryPoint = "clGetDeviceInfo")] public static extern int GetDevStr(IntPtr dev, uint param, IntPtr size, StringBuilder val, out IntPtr ret);
        [DllImport(DLL, EntryPoint = "clGetDeviceInfo")] public static extern int GetDevU64(IntPtr dev, uint param, IntPtr size, out ulong val, out IntPtr ret);
        [DllImport(DLL)] public static extern IntPtr clCreateContext(IntPtr props, uint n, IntPtr[] devs, IntPtr notify, IntPtr user, out int err);
        [DllImport(DLL)] public static extern IntPtr clCreateCommandQueue(IntPtr ctx, IntPtr dev, ulong props, out int err);
        [DllImport(DLL)] public static extern IntPtr clCreateProgramWithSource(IntPtr ctx, uint count, string[] src, IntPtr[] lens, out int err);
        [DllImport(DLL)] public static extern int clBuildProgram(IntPtr prog, uint n, IntPtr[] devs, string opts, IntPtr notify, IntPtr user);
        [DllImport(DLL)] public static extern int clGetProgramBuildInfo(IntPtr prog, IntPtr dev, uint param, IntPtr size, StringBuilder log, out IntPtr ret);
        [DllImport(DLL)] public static extern IntPtr clCreateKernel(IntPtr prog, string name, out int err);
        [DllImport(DLL)] public static extern IntPtr clCreateBuffer(IntPtr ctx, ulong flags, IntPtr size, IntPtr host, out int err);
        [DllImport(DLL, EntryPoint = "clSetKernelArg")] public static extern int ArgMem(IntPtr k, uint i, IntPtr size, ref IntPtr v);
        [DllImport(DLL, EntryPoint = "clSetKernelArg")] public static extern int ArgInt(IntPtr k, uint i, IntPtr size, ref int v);
        [DllImport(DLL)] public static extern int clEnqueueNDRangeKernel(IntPtr q, IntPtr k, uint dim, IntPtr[] off, IntPtr[] gsz, IntPtr[] lsz, uint nev, IntPtr evs, IntPtr ev);
        [DllImport(DLL)] public static extern int clFinish(IntPtr q);
        [DllImport(DLL)] public static extern int clReleaseMemObject(IntPtr m);
        [DllImport(DLL)] public static extern int clReleaseKernel(IntPtr k);
        [DllImport(DLL)] public static extern int clReleaseProgram(IntPtr p);
        [DllImport(DLL)] public static extern int clReleaseCommandQueue(IntPtr q);
        [DllImport(DLL)] public static extern int clReleaseContext(IntPtr c);
    }

    // ------------------------------------------------------------------
    //  Janela de render do teste de estresse (visual como FurMark / Cinebench)
    // ------------------------------------------------------------------
    class BenchCanvas : Control
    {
        public bool HudOnly; public int Kind; public double Rate, Temp = double.NaN, Elapsed, Total; public string Unit = "", Extra = ""; public long Errors;
        static readonly string[] Names = { "GPU - render 3D de pelos (OpenGL)", "CPU - render 3D por blocos (ray tracing)", "FPU - malha de ondas em ponto flutuante", "RAM - mapa de memoria", "SSD - mapa de blocos do disco" };
        Timer tm = new Timer();
        DateTime t0 = DateTime.UtcNow, fpsT = DateTime.UtcNow; int frames; double fps;
        // pelos
        const int HN = 1400;
        double[] hu = new double[HN], hv = new double[HN], hs = new double[HN];
        Pen[] furPens = new Pen[8];
        // render por blocos (ray tracing)
        const int W = 1280, H = 800, B = 40, NBX = W / B, NBY = H / B, NB = NBX * NBY;
        int[] pix = new int[W * H]; int[] bstate = new int[NB];
        double lastPass, bestPass; DateTime passStart = DateTime.UtcNow;
        int nextB, doneB, pass; DateTime finishedAt = DateTime.MinValue; volatile bool quit; object lk = new object();
        Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppRgb);

        public BenchCanvas(int kind, int threads)
        {
            Kind = kind;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
            Random r = new Random(11);
            for (int i = 0; i < HN; i++) { hu[i] = r.NextDouble() * Math.PI * 2; hv[i] = r.NextDouble() * Math.PI * 2; hs[i] = r.NextDouble(); }
            for (int i = 0; i < 8; i++) furPens[i] = new Pen(Color.FromArgb(255, 90 + i * 22, 10 + i * 10, 0), 1f);
            for (int i = 0; i < pix.Length; i++) pix[i] = unchecked((int)0xFF303030);
            tm.Interval = 50; tm.Tick += delegate { Invalidate(); }; tm.Start();
            if (kind == 2)
            {
                fpuN = Math.Max(1, threads);
                for (int i = 0; i < fpuN; i++)
                {
                    Th.Thread ft = new Th.Thread(FpuLoop); ft.IsBackground = true; ft.Start(i);
                }
            }
            if (kind == 1)
                for (int i = 0; i < Math.Max(1, threads); i++)
                {
                    Th.Thread t = new Th.Thread(CpuLoop); t.IsBackground = true; t.Priority = Th.ThreadPriority.Normal; t.Start();
                }
        }

        public void SavePng()
        {
            using (SaveFileDialog sd = new SaveFileDialog())
            {
                sd.Filter = "PNG|*.png"; sd.FileName = "render_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".png";
                if (sd.ShowDialog() != DialogResult.OK) return;
                try
                {
                    using (Bitmap c = new Bitmap(W, H, PixelFormat.Format32bppRgb))
                    {
                        BitmapData d = c.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                        Marshal.Copy(pix, 0, d.Scan0, pix.Length); c.UnlockBits(d); c.Save(sd.FileName, ImageFormat.Png);
                    }
                }
                catch (Exception) { }
            }
        }

        protected override void Dispose(bool d)
        {
            quit = true; tm.Stop();
            if (d) { bmp.Dispose(); foreach (Pen p in furPens) p.Dispose(); }
            base.Dispose(d);
        }

        // ---------- FPU: campo de ondas calculado em ponto flutuante pelas threads; a malha desenhada vem deste campo ----------
        const int FN = 320; float[] field = new float[FN * FN]; volatile int fpuN = 1; double ph;
        void FpuLoop(object o)
        {
            int w = (int)o;
            while (!quit && !Bench.Stop)
            {
                int n = fpuN; double p = ph;
                for (int i = w; i < FN; i += n)
                {
                    double x = (i - FN / 2.0) / (FN / 2.0);
                    for (int j = 0; j < FN; j++)
                    {
                        double z = (j - FN / 2.0) / (FN / 2.0), r = Math.Sqrt(x * x + z * z);
                        double h = 0.35 * Math.Sin(6 * r - p * 2) * Math.Exp(-r * 1.2) + 0.2 * Math.Sin(3 * x + p) * Math.Cos(3 * z + p * 0.7);
                        for (int k = 1; k < 8; k++) h += 0.01 * Math.Sin(h * k + x) * Math.Cos(z * k + p);
                        field[i * FN + j] = (float)h;
                    }
                }
                Th.Interlocked.Add(ref Bench.Counter, (long)(FN / n + 1) * FN * 200);
                if (w == 0) ph += 0.08;
            }
        }

        // ---------- CPU: ray tracing 3D por blocos (como o Cinebench / Blender), todas as threads renderizando ----------
        static readonly double[] SX = { 0, -2.2, 2.1, -0.8, 0.9 }, SY = { 1.0, 0.7, 0.7, 0.35, 0.45 }, SZ = { 0, 0.6, 0.5, -1.8, -1.6 }, SRd = { 1.0, 0.7, 0.7, 0.35, 0.45 };
        static readonly double[] SCR = { 0.9, 0.2, 0.9, 0.2, 0.85 }, SCG = { 0.25, 0.5, 0.8, 0.85, 0.85 }, SCB = { 0.2, 0.9, 0.2, 0.4, 0.9 }, SRF = { 0.35, 0.5, 0.25, 0.6, 0.8 };
        double camE0, camE1, camE2, camF0, camF1, camF2, camR0, camR2, camU0, camU1, camU2; int[] order; const int SPP = 32;

        void SetupPass()
        {
            double a = 0.7 + pass * 0.85;
            camE0 = Math.Sin(a) * 6.2; camE1 = 2.3 + 0.8 * Math.Sin(pass * 1.3); camE2 = -Math.Cos(a) * 6.2;
            double tx = 0, ty = 0.8, tz = 0;
            double fx = tx - camE0, fy = ty - camE1, fz = tz - camE2, fl = Math.Sqrt(fx * fx + fy * fy + fz * fz);
            camF0 = fx / fl; camF1 = fy / fl; camF2 = fz / fl;
            double rl = Math.Sqrt(camF0 * camF0 + camF2 * camF2);
            camR0 = -camF2 / rl; camR2 = camF0 / rl;
            camU0 = -camR2 * camF1; camU1 = camR2 * camF0 - camR0 * camF2; camU2 = camR0 * camF1;
        }

        static bool Occluded(double ox, double oy, double oz, double dx, double dy, double dz)
        {
            for (int i = 0; i < 5; i++)
            {
                double lx = ox - SX[i], ly = oy - SY[i], lz = oz - SZ[i];
                double b = lx * dx + ly * dy + lz * dz, c = lx * lx + ly * ly + lz * lz - SRd[i] * SRd[i], disc = b * b - c;
                if (disc > 0 && -b - Math.Sqrt(disc) > 1e-4) return true;
            }
            return false;
        }

        static void Trace(double ox, double oy, double oz, double dx, double dy, double dz, ref long rays, out double R, out double G, out double Bl)
        {
            double ar = 0, ag = 0, ab = 0, att = 1;
            const double LX = 0.5121, LY = 0.8193, LZ = -0.2561;
            for (int depth = 0; depth < 5; depth++)
            {
                rays++;
                double bt = 1e30; int hit = -2;
                for (int i = 0; i < 5; i++)
                {
                    double lx = ox - SX[i], ly = oy - SY[i], lz = oz - SZ[i];
                    double b = lx * dx + ly * dy + lz * dz, c = lx * lx + ly * ly + lz * lz - SRd[i] * SRd[i], disc = b * b - c;
                    if (disc > 0) { double t = -b - Math.Sqrt(disc); if (t > 1e-4 && t < bt) { bt = t; hit = i; } }
                }
                if (dy < -1e-6) { double t = -oy / dy; if (t > 1e-4 && t < bt) { bt = t; hit = -1; } }
                if (hit == -2)
                {
                    double k = 0.5 * (dy + 1);
                    ar += att * (0.75 - 0.35 * k); ag += att * (0.85 - 0.25 * k); ab += att * (1.0 - 0.1 * k);
                    break;
                }
                double px = ox + dx * bt, py = oy + dy * bt, pz = oz + dz * bt, nx, ny, nz, cr, cg, cb, refl;
                if (hit == -1)
                {
                    nx = 0; ny = 1; nz = 0;
                    int ck = ((int)Math.Floor(px) + (int)Math.Floor(pz)) & 1;
                    cr = cg = cb = ck == 0 ? 0.85 : 0.18; refl = 0.22;
                }
                else
                {
                    nx = (px - SX[hit]) / SRd[hit]; ny = (py - SY[hit]) / SRd[hit]; nz = (pz - SZ[hit]) / SRd[hit];
                    cr = SCR[hit]; cg = SCG[hit]; cb = SCB[hit]; refl = SRF[hit];
                }
                double diff = nx * LX + ny * LY + nz * LZ;
                if (diff < 0) diff = 0;
                if (diff > 0) { rays++; if (Occluded(px + nx * 1e-3, py + ny * 1e-3, pz + nz * 1e-3, LX, LY, LZ)) diff *= 0.2; }
                double dn = dx * nx + dy * ny + dz * nz;
                double rx = dx - 2 * dn * nx, ry = dy - 2 * dn * ny, rz = dz - 2 * dn * nz;
                double spec = rx * LX + ry * LY + rz * LZ; spec = spec > 0 ? Math.Pow(spec, 40) * 0.6 : 0;
                double lum = 0.2 + 0.8 * diff, w = att * (1 - refl);
                ar += w * (cr * lum + spec); ag += w * (cg * lum + spec); ab += w * (cb * lum + spec);
                att *= refl;
                if (att < 0.03) break;
                ox = px; oy = py; oz = pz; dx = rx; dy = ry; dz = rz;
            }
            R = ar; G = ag; Bl = ab;
        }

        int Take()
        {
            lock (lk)
            {
                if (order == null)
                {
                    List<int> l = new List<int>();
                    for (int i = 0; i < NB; i++) l.Add(i);
                    l.Sort(delegate(int p, int q)
                    {
                        double dp = Math.Pow(p % NBX - NBX / 2.0 + 0.5, 2) + Math.Pow(p / NBX - NBY / 2.0 + 0.5, 2), dq = Math.Pow(q % NBX - NBX / 2.0 + 0.5, 2) + Math.Pow(q / NBX - NBY / 2.0 + 0.5, 2);
                        return dp.CompareTo(dq);
                    });
                    order = l.ToArray(); SetupPass();
                }
                if (nextB < NB) { int b = order[nextB++]; bstate[b] = 1; return b; }
                if (doneB >= NB && (DateTime.UtcNow - finishedAt).TotalSeconds > 2.0)
                {
                    pass++; SetupPass();
                    nextB = 0; doneB = 0; passStart = DateTime.UtcNow;
                    for (int i = 0; i < NB; i++) bstate[i] = 0;
                    for (int i = 0; i < pix.Length; i++) pix[i] = unchecked((int)0xFF202020);
                }
                return -1;
            }
        }
        void CpuLoop()
        {
            Random rnd = new Random(Th.Thread.CurrentThread.ManagedThreadId * 7919 + Environment.TickCount);
            while (!quit && !Bench.Stop)
            {
                int b = Take();
                if (b < 0) { Th.Thread.Sleep(30); continue; }
                int x0 = (b % NBX) * B, y0 = (b / NBX) * B; long rays = 0;
                double ex = camE0, ey = camE1, ez = camE2, fx = camF0, fy = camF1, fz = camF2, rx = camR0, rz = camR2, ux = camU0, uy = camU1, uz = camU2;
                double th = 0.45, asp = W / (double)H;
                for (int y = y0; y < y0 + B && !quit && !Bench.Stop; y++)
                    for (int x = x0; x < x0 + B; x++)
                    {
                        double sr = 0, sg = 0, sb = 0;
                        for (int s = 0; s < SPP; s++)
                        {
                            double u = ((x + rnd.NextDouble()) / W * 2 - 1) * asp * th, v = (1 - (y + rnd.NextDouble()) / H * 2) * th;
                            double dx = fx + rx * u + ux * v, dy = fy + uy * v, dz = fz + rz * u + uz * v, dl = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                            double cr, cg, cb;
                            Trace(ex, ey, ez, dx / dl, dy / dl, dz / dl, ref rays, out cr, out cg, out cb);
                            sr += cr; sg += cg; sb += cb;
                        }
                        int ir = (int)(255 * Math.Sqrt(Math.Min(1, sr / SPP))), ig = (int)(255 * Math.Sqrt(Math.Min(1, sg / SPP))), ib = (int)(255 * Math.Sqrt(Math.Min(1, sb / SPP)));
                        pix[y * W + x] = unchecked((int)0xFF000000) | (ir << 16) | (ig << 8) | ib;
                    }
                Th.Interlocked.Add(ref Bench.Counter, rays);
                lock (lk) { bstate[b] = 2; doneB++; if (doneB >= NB) { finishedAt = DateTime.UtcNow; lastPass = (finishedAt - passStart).TotalSeconds; if (bestPass <= 0 || lastPass < bestPass) bestPass = lastPass; } }
            }
        }

        // ---------- desenho ----------
        static PointF Proj(double x, double y, double z, double ox, double oy, double sc, out double depth)
        {
            depth = z;
            double k = sc / (z + 4.2);
            return new PointF((float)(ox + x * k), (float)(oy - y * k));
        }

        void DrawFur(Graphics g, Rectangle r, double t)
        {
            double ay = t * 0.9, ax = 0.55 + 0.3 * Math.Sin(t * 0.5);
            double cay = Math.Cos(ay), say = Math.Sin(ay), cax = Math.Cos(ax), sax = Math.Sin(ax);
            double ox = r.X + r.Width / 2.0, oy = r.Y + r.Height / 2.0, sc = Math.Min(r.Width, r.Height) * 0.62;
            using (GraphicsPath gl = new GraphicsPath())
            {
                gl.AddEllipse((float)(ox - sc * 0.55), (float)(oy - sc * 0.55), (float)(sc * 1.1), (float)(sc * 1.1));
                using (PathGradientBrush pb = new PathGradientBrush(gl))
                {
                    pb.CenterColor = Color.FromArgb(70, 120, 30, 0); pb.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) };
                    g.FillPath(pb, gl);
                }
            }
            for (int i = 0; i < HN; i++)
            {
                double u = hu[i], v = hv[i];
                double R = 1.0, rr = 0.42;
                double nx = Math.Cos(v) * Math.Cos(u), ny = Math.Sin(v), nz = Math.Cos(v) * Math.Sin(u);
                double px = (R + rr * Math.Cos(v)) * Math.Cos(u), py = rr * Math.Sin(v), pz = (R + rr * Math.Cos(v)) * Math.Sin(u);
                double len = 0.24 + 0.08 * Math.Sin(t * 4 + hs[i] * 6.28);
                double ex = px + nx * len + 0.03 * Math.Sin(t * 3 + i), ey = py + ny * len, ez = pz + nz * len;
                // gira em Y e depois em X
                double x1 = px * cay + pz * say, z1 = -px * say + pz * cay, y1 = py * cax - z1 * sax, z2 = py * sax + z1 * cax;
                double x2 = ex * cay + ez * say, z3 = -ex * say + ez * cay, y2 = ey * cax - z3 * sax, z4 = ey * sax + z3 * cax;
                double d1, d2;
                PointF a = Proj(x1, y1, z2, ox, oy, sc, out d1), b = Proj(x2, y2, z4, ox, oy, sc, out d2);
                int idx = (int)((1 - (d2 + 1.6) / 3.2) * 7); if (idx < 0) idx = 0; if (idx > 7) idx = 7;
                g.DrawLine(furPens[idx], a, b);
            }
        }

        void DrawWave(Graphics g, Rectangle r, double t)
        {
            int n = 30;
            double ay = t * 0.5, ax = 0.9;
            double cay = Math.Cos(ay), say = Math.Sin(ay), cax = Math.Cos(ax), sax = Math.Sin(ax);
            double ox = r.X + r.Width / 2.0, oy = r.Y + r.Height / 2.0 + 20, sc = Math.Min(r.Width, r.Height) * 0.75;
            PointF[,] pts = new PointF[n, n]; double[,] hh = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    double x = (i - n / 2.0) / (n / 2.0) * 1.3, z = (j - n / 2.0) / (n / 2.0) * 1.3;
                    double h = field[(int)(i * (FN - 1) / (n - 1.0)) * FN + (int)(j * (FN - 1) / (n - 1.0))] * 1.4;
                    double x1 = x * cay + z * say, z1 = -x * say + z * cay, y1 = h * cax - z1 * sax, z2 = h * sax + z1 * cax, d;
                    pts[i, j] = Proj(x1, y1, z2, ox, oy, sc, out d); hh[i, j] = h;
                }
            using (Pen p1 = new Pen(Color.FromArgb(60, 200, 90)), p2 = new Pen(Color.FromArgb(120, 255, 140)), p3 = new Pen(Color.FromArgb(220, 255, 200)))
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                    {
                        Pen p = hh[i, j] > 0.2 ? p3 : (hh[i, j] > 0 ? p2 : p1);
                        if (i + 1 < n) g.DrawLine(p, pts[i, j], pts[i + 1, j]);
                        if (j + 1 < n) g.DrawLine(p, pts[i, j], pts[i, j + 1]);
                    }
        }

        void DrawCpu(Graphics g, Rectangle r)
        {
            System.Drawing.Imaging.BitmapData d = bmp.LockBits(new Rectangle(0, 0, W, H), System.Drawing.Imaging.ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            Marshal.Copy(pix, 0, d.Scan0, pix.Length);
            bmp.UnlockBits(d);
            float s = Math.Min(r.Width / (float)W, r.Height / (float)H);
            float w = W * s, h = H * s, x0 = r.X + (r.Width - w) / 2, y0 = r.Y + (r.Height - h) / 2;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(bmp, x0, y0, w, h);
            using (Pen p = new Pen(Color.White, 2f))
                for (int b = 0; b < NB; b++)
                    if (bstate[b] == 1) g.DrawRectangle(p, x0 + (b % NBX) * B * s, y0 + (b / NBX) * B * s, B * s, B * s);
            using (Pen fr = new Pen(Color.FromArgb(120, 120, 120))) g.DrawRectangle(fr, x0, y0, w, h);
            string st = "Render #" + (pass + 1) + "   blocos: " + doneB + "/" + NB + "   tempo: " + (doneB >= NB ? lastPass : (DateTime.UtcNow - passStart).TotalSeconds).ToString("0.0") + " s";
            if (lastPass > 0) st += "   ultimo: " + lastPass.ToString("0.0") + " s   melhor: " + bestPass.ToString("0.0") + " s";
            TextRenderer.DrawText(g, Lang.T(st), SystemFonts.DefaultFont, new Point((int)x0 + 4, (int)y0 + 4), Color.White, Color.FromArgb(150, 0, 0, 0));
        }

        // RAM: modulo de memoria com chips sendo preenchidos + fluxo de palavras hexadecimais sendo gravadas/conferidas
        void DrawRam(Graphics g, Rectangle r, double t)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int ph = (int)(t / 4);
            Color[] pal = { Color.FromArgb(40, 140, 255), Color.FromArgb(60, 200, 90), Color.FromArgb(240, 190, 40), Color.FromArgb(200, 90, 220) };
            Color pc = pal[ph % pal.Length];
            Rectangle mod = new Rectangle(r.X + 10, r.Y + 6, r.Width - 20, Math.Max(60, r.Height * 2 / 5));
            using (LinearGradientBrush pg = new LinearGradientBrush(mod, Color.FromArgb(30, 120, 70), Color.FromArgb(10, 70, 40), 90f)) g.FillRectangle(pg, mod);
            g.DrawRectangle(Pens.DarkGreen, mod);
            int chips = 8; float cw = (mod.Width - 20) / (float)chips;
            for (int i = 0; i < chips; i++)
            {
                RectangleF c = new RectangleF(mod.X + 10 + i * cw + 4, mod.Y + 14, cw - 8, mod.Height - 40);
                g.FillRectangle(Brushes.Black, c);
                double lv = (t * 0.45 + i * 0.13) % 1.0;
                bool verify = ((int)(t * 0.45 + i * 0.13) & 1) == 1;
                using (SolidBrush fb = new SolidBrush(verify ? Color.FromArgb(230, 230, 230) : pc))
                    g.FillRectangle(fb, c.X + 2, c.Bottom - 2 - (c.Height - 4) * (float)lv, c.Width - 4, (c.Height - 4) * (float)lv);
                g.DrawRectangle(Pens.DimGray, c.X, c.Y, c.Width, c.Height);
            }
            using (SolidBrush gold = new SolidBrush(Color.FromArgb(235, 190, 60)))
                for (int i = 0; i < 40; i++) g.FillRectangle(gold, mod.X + 8 + i * (mod.Width - 16) / 40f, mod.Bottom - 10, (mod.Width - 16) / 40f * 0.6f, 10);
            string mode = (Extra ?? "");
            using (Font f = new Font("Consolas", 10f, FontStyle.Bold))
            {
                int rows = Math.Max(4, (r.Bottom - mod.Bottom - 16) / 16);
                uint seed = (uint)(t * 9);
                for (int ry = 0; ry < rows; ry++)
                {
                    StringBuilder sb = new StringBuilder();
                    for (int k = 0; k < 10; k++)
                    {
                        uint v = unchecked((seed + (uint)ry * 2654435761u + (uint)k * 40503u) * 2246822519u); v ^= v >> 15;
                        sb.Append(((ph + ry) & 1) == 0 ? (k % 2 == 0 ? "AAAAAAAA " : "55555555 ") : v.ToString("X8") + " ");
                    }
                    int a = 255 - ry * 255 / (rows + 2);
                    TextRenderer.DrawText(g, sb.ToString(), f, new Point(r.X + 14, mod.Bottom + 10 + ry * 16), Color.FromArgb(a / 2 + 40, pc.R, pc.G, pc.B));
                }
            }
        }

        List<double> ssdHist = new List<double>(); DateTime ssdHistT = DateTime.MinValue;
        // SSD: faixa sequencial com cabecote, grafico de velocidade ao vivo e mapa de acessos 4K aleatorios
        void DrawSsd(Graphics g, Rectangle r, double t)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if ((DateTime.UtcNow - ssdHistT).TotalMilliseconds >= 150) { ssdHistT = DateTime.UtcNow; ssdHist.Add(Rate); if (ssdHist.Count > 160) ssdHist.RemoveAt(0); }
            string ex = Extra ?? "";
            bool rnd = ex.IndexOf("4K") >= 0, read = ex.IndexOf("Leitura") >= 0 && !rnd;
            Color acc = rnd ? Color.FromArgb(240, 200, 40) : (read ? Color.FromArgb(60, 200, 110) : Color.FromArgb(70, 150, 255));
            // 1) faixa do arquivo (setores) com o cabecote
            Rectangle lane = new Rectangle(r.X + 10, r.Y + 8, r.Width - 20, 34);
            using (SolidBrush lb = new SolidBrush(Color.FromArgb(24, 24, 28))) g.FillRectangle(lb, lane);
            int segs = 80; float sw = lane.Width / (float)segs; int head = (int)(t * 18) % segs;
            for (int i = 0; i < segs; i++)
            {
                bool done = rnd ? (((i * 2654435761u) >> 7) + (uint)(t * 5)) % 5 == 0 : i < head;
                using (SolidBrush sb = new SolidBrush(done ? Color.FromArgb(150, acc) : Color.FromArgb(40, 40, 46))) g.FillRectangle(sb, lane.X + i * sw + 1, lane.Y + 3, sw - 2, lane.Height - 6);
            }
            if (!rnd) using (Pen hp = new Pen(Color.White, 2f)) g.DrawLine(hp, lane.X + head * sw, lane.Y, lane.X + head * sw, lane.Bottom);
            g.DrawRectangle(Pens.DimGray, lane);
            // 2) grafico de velocidade
            Rectangle gr = new Rectangle(r.X + 10, lane.Bottom + 12, (r.Width - 30) * 3 / 5, r.Bottom - lane.Bottom - 20);
            using (SolidBrush gb = new SolidBrush(Color.FromArgb(18, 18, 22))) g.FillRectangle(gb, gr);
            using (Pen gp = new Pen(Color.FromArgb(50, 50, 58))) for (int i = 1; i < 4; i++) g.DrawLine(gp, gr.X, gr.Y + gr.Height * i / 4, gr.Right, gr.Y + gr.Height * i / 4);
            if (ssdHist.Count > 2)
            {
                double mx = 1; foreach (double v in ssdHist) if (v > mx) mx = v;
                PointF[] pts = new PointF[ssdHist.Count + 2];
                for (int i = 0; i < ssdHist.Count; i++) pts[i + 1] = new PointF(gr.X + i * gr.Width / 159f, gr.Bottom - (float)(ssdHist[i] / mx) * (gr.Height - 6) - 2);
                pts[0] = new PointF(pts[1].X, gr.Bottom); pts[pts.Length - 1] = new PointF(pts[pts.Length - 2].X, gr.Bottom);
                using (SolidBrush fb = new SolidBrush(Color.FromArgb(70, acc))) g.FillPolygon(fb, pts);
                using (Pen lp = new Pen(acc, 2f)) { PointF[] ln = new PointF[ssdHist.Count]; Array.Copy(pts, 1, ln, 0, ln.Length); g.DrawLines(lp, ln); }
            }
            g.DrawRectangle(Pens.DimGray, gr);
            // 3) mapa de blocos 4K acessados
            Rectangle mp = new Rectangle(gr.Right + 10, gr.Y, r.Right - gr.Right - 20, gr.Height);
            int cols = 24, rows = Math.Max(4, mp.Height / 12); float cw = mp.Width / (float)cols, ch = mp.Height / (float)rows;
            using (SolidBrush mb = new SolidBrush(Color.Black))
                for (int i = 0; i < cols * rows; i++)
                {
                    int hsh = (int)(unchecked((uint)(i + (int)(t * (rnd ? 14 : 3))) * 2654435761u) >> 24);
                    bool on = rnd ? hsh < 70 : (i % cols) < ((int)(t * 6) % cols) && (i / cols) == ((int)(t * 6) / cols) % rows;
                    mb.Color = on ? acc : Color.FromArgb(30, 30, 36);
                    g.FillRectangle(mb, mp.X + (i % cols) * cw + 1, mp.Y + (i / cols) * ch + 1, cw - 2, ch - 2);
                }
        }

        void DrawGrid(Graphics g, Rectangle r, int cols, int rows, double t)
        {
            int cells = cols * rows;
            float cw = r.Width / (float)cols, ch = r.Height / (float)rows;
            Color[] pal = { Color.FromArgb(40, 90, 200), Color.FromArgb(40, 170, 80), Color.FromArgb(200, 160, 30), Color.FromArgb(150, 60, 190), Color.FromArgb(30, 160, 170), Color.FromArgb(200, 90, 40) };
            int pos = (int)(t * (Kind == 3 ? 220 : 160));
            int cur = pos / cells, head = pos % cells;
            string ex = Extra ?? "";
            bool rnd = Kind == 4 && ex.IndexOf("4K") >= 0, read = Kind == 4 && ex.IndexOf("Leitura") >= 0 && !rnd;
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(28, 28, 28)), br = new SolidBrush(Color.Black))
            {
                for (int i = 0; i < cells; i++)
                {
                    int c = i < head ? cur : cur - 1;
                    Color col = c < 0 ? Color.FromArgb(28, 28, 28) : pal[(Kind == 4 ? (read ? 1 : 0) : c) % pal.Length];
                    if (rnd) { int hsh = (int)(unchecked((uint)(i + (int)(t * 12)) * 2654435761u) >> 24); col = hsh < 90 ? Color.FromArgb(230, 200, 40) : Color.FromArgb(28, 28, 28); }
                    if (i == head) col = Color.White;
                    br.Color = col;
                    g.FillRectangle(br, r.X + (i % cols) * cw + 1, r.Y + (i / cols) * ch + 1, cw - 2, ch - 2);
                }
                if (Errors > 0)
                {
                    br.Color = Color.Red;
                    for (int k = 0; k < Math.Min(Errors, 40); k++)
                    {
                        int i = (int)((k * 977L + 13) % cells);
                        g.FillRectangle(br, r.X + (i % cols) * cw + 1, r.Y + (i / cols) * ch + 1, cw - 2, ch - 2);
                    }
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.FromArgb(10, 10, 12));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            double t = (DateTime.UtcNow - t0).TotalSeconds;
            frames++;
            if ((DateTime.UtcNow - fpsT).TotalSeconds >= 1) { fps = frames / (DateTime.UtcNow - fpsT).TotalSeconds; frames = 0; fpsT = DateTime.UtcNow; }
            Rectangle area = new Rectangle(10, 34, Width - 20, Height - 34 - 74);
            if (!HudOnly) switch (Kind)
            {
                case 0: DrawFur(g, area, t); break;
                case 1: DrawCpu(g, area); break;
                case 2: DrawWave(g, area, t); break;
                case 3: DrawRam(g, area, t); break;
                default: DrawSsd(g, area, t); break;
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Font f = new Font("Tahoma", 10f, FontStyle.Bold), fb = new Font("Tahoma", 16f, FontStyle.Bold), fs = new Font("Tahoma", 8.25f))
            {
                TextRenderer.DrawText(g, Lang.T(Names[Kind < 5 ? Kind : 0]), f, new Point(12, 8), Color.FromArgb(255, 200, 60));
                string rt = Rate.ToString("0.0") + " " + Unit;
                TextRenderer.DrawText(g, rt, fb, new Point(12, Height - 70), Color.White);
                string tt = double.IsNaN(Temp) ? "-" : Temp.ToString("0") + " °C";
                int es = (int)Elapsed, tot = (int)Total;
                string tm2 = (es / 60).ToString("00") + ":" + (es % 60).ToString("00") + (tot > 0 ? " / " + (tot / 60).ToString("00") + ":" + (tot % 60).ToString("00") : "");
                string info = "Temperatura: " + tt + "     Tempo: " + tm2 + "     Erros: " + Errors + (Kind == 0 || Kind == 2 ? "     FPS do render: " + fps.ToString("0") : "") + (Kind == 1 ? "     Pontos: " + (Rate * 100).ToString("0") + " pts" : "");
                TextRenderer.DrawText(g, Lang.T(info), fs, new Point(12, Height - 36), Color.FromArgb(200, 200, 200));
                if (!string.IsNullOrEmpty(Extra)) TextRenderer.DrawText(g, Lang.T(Extra), fs, new Point(12, Height - 20), Color.FromArgb(150, 150, 150));
            }
        }
    }

    class RenderForm : XpForm
    {
        public BenchCanvas Cv; public GlCanvas Gl;
        public RenderForm(int kind, int threads)
        {
            Text = "BunoTunner - Render do teste (Esc fecha e para)";
            Size = new Size(700, 560);
            Cv = new BenchCanvas(kind, threads);
            if (kind == 0)
            {
                Cv.HudOnly = true; Cv.Dock = DockStyle.Bottom; Cv.Height = 110;
                Gl = new GlCanvas(); Gl.Dock = DockStyle.Fill;
                Controls.Add(Gl); Controls.Add(Cv);
            }
            else { Cv.Dock = DockStyle.Fill; Controls.Add(Cv); }
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            if (kind == 1)
            {
                ContextMenuStrip cm = new ContextMenuStrip();
                cm.Items.Add("Salvar imagem do render (PNG)", null, delegate { Cv.SavePng(); });
                Lang.Menu(cm); Cv.ContextMenuStrip = cm;
            }
        }
    }

    // ------------------------------------------------------------------
    //  PawnIO: driver assinado usado pelas versoes novas do LibreHardwareMonitor
    //  (temperatura da CPU, controle de ventoinhas da placa-mae, leitura SMN/timings).
    // ------------------------------------------------------------------
    static class PawnIo
    {
        public static bool LibUsesPawn()
        {
            try
            {
                foreach (Type t in typeof(LHM.Computer).Assembly.GetTypes())
                    if (t.Name.Equals("PawnIo", StringComparison.OrdinalIgnoreCase) || (t.Namespace != null && t.Namespace.EndsWith("PawnIo", StringComparison.OrdinalIgnoreCase))) return true;
            }
            catch (Exception) { }
            return false;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tpl);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        public static bool Installed()
        {
            string[] pf = { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetEnvironmentVariable("ProgramFiles(x86)") };
            foreach (string p in pf)
                if (!string.IsNullOrEmpty(p) && File.Exists(Path.Combine(Path.Combine(p, "PawnIO"), "PawnIOLib.dll"))) return true;
            try
            {
                IntPtr h = CreateFileW("\\\\.\\PawnIO", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (h != IntPtr.Zero && h != (IntPtr)(-1)) { CloseHandle(h); return true; }
            }
            catch (Exception) { }
            return false;
        }

        // Baixa e executa o instalador oficial (pede permissao de administrador). Chamado pelo botao do menu Ventoinhas.
        public static void InstallNow(Control owner)
        {
            if (Installed()) { Dlg.Show(owner, "O PawnIO ja esta instalado.", "BunoTunner", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (Dlg.Show(owner, "Baixar e instalar o PawnIO agora?\n\nO instalador oficial (github.com/namazso/PawnIO.Setup) sera baixado e pedira permissao de administrador.", "BunoTunner - PawnIO", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            string exe = Path.Combine(Path.GetTempPath(), "PawnIO_setup.exe");
            owner.Enabled = false;
            Th.ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null; bool done = false;
                try
                {
                    System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072 | (System.Net.SecurityProtocolType)768;
                    using (System.Net.WebClient wc = new System.Net.WebClient())
                        wc.DownloadFile("https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe", exe);
                    ProcessStartInfo psi = new ProcessStartInfo(exe); psi.UseShellExecute = true; psi.Verb = "runas";
                    using (Process p = Process.Start(psi)) { if (p != null) { p.WaitForExit(); done = true; } }
                }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    owner.BeginInvoke(new Action(delegate
                    {
                        owner.Enabled = true;
                        if (err != null)
                        {
                            if (Dlg.Show(owner, "Nao foi possivel instalar automaticamente (" + err + ").\nAbrir o download no navegador?", "BunoTunner", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                                try { Process.Start("https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe"); } catch (Exception) { }
                        }
                        else if (done) Dlg.Show(owner, Installed() ? "PawnIO instalado! Feche e abra o BunoTunner para os sensores, ventoinhas e timings passarem a usar o driver." : "O instalador foi fechado, mas o PawnIO nao foi detectado. Tente novamente.", "BunoTunner", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch (Exception) { }
            });
        }

        // Chamado em toda inicializacao: se o PawnIO nao estiver instalado, recomenda e leva ao download.
        public static void CheckAtStartup(IWin32Window owner)
        {
            if (Installed()) return;
            DialogResult r = Dlg.Show(owner,
                "Recomendamos a instalacao do PawnIO.\n\n" +
                "Por que?\n" +
                "O PawnIO e um driver assinado e de codigo aberto que o LibreHardwareMonitor (usado pelo BunoTunner) precisa para acessar o hardware. " +
                "Sem ele ficam sem leitura: varios sensores de temperatura da CPU e da placa-mae, o controle das ventoinhas ligadas a placa-mae e os timings da memoria RAM.\n\n" +
                "Deseja abrir o download oficial agora?\n(Sim = baixa o PawnIO_setup.exe; Nao = continuar sem ele. Este aviso aparece a cada abertura enquanto ele nao estiver instalado.)",
                "BunoTunner - PawnIO recomendado", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r != DialogResult.Yes) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe");
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception)
            {
                try { Process.Start("https://pawnio.eu"); } catch (Exception) { }
            }
            Dlg.Show(owner, "Depois do download, execute o PawnIO_setup.exe (como administrador), conclua a instalacao e abra o BunoTunner novamente.", "BunoTunner", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ------------------------------------------------------------------
    //  Fundos estaticos dos temas (Neon e GTA VI: pixel art; Steampunk: tubos de cobre, manometros e engrenagens)
    // ------------------------------------------------------------------
    static class Fx
    {
        // Fundos dos temas (estaticos, sem animacao): Neon e GTA VI em pixel art, Steampunk em arte realista
        static List<Control> targets = new List<Control>();
        static Bitmap sNeon, sVice, sSteam;
        static bool failed;
        public static bool On { get { return (Xp.Theme >= 3 && Xp.Theme <= 4) || Xp.Theme == 6; } }
        static Color A(int a, int r, int g, int b) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), r, g, b); }
        static Color Hex(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
        static Color HexA(int a, int rgb) { return Color.FromArgb(a, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
        static Color Lp(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
        static Color Ramp(Color[] st, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            double f = t * (st.Length - 1);
            int i = (int)f;
            if (i >= st.Length - 1) return st[st.Length - 1];
            return Lp(st[i], st[i + 1], f - i);
        }

        public static void Hook(Control c)
        {
            if (targets.Contains(c)) return;
            targets.Add(c);
            c.Disposed += delegate { targets.Remove(c); };
            if (c.GetType() == typeof(Panel))
            {
                try { typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(c, true, null); } catch (Exception) { }
                c.Paint += delegate(object s, PaintEventArgs e) { Draw(c, e.Graphics); };
            }
        }

        static Bitmap Scene()
        {
            if (failed) return null;
            try
            {
                switch (Xp.Theme)
                {
                    case 3: if (sNeon == null) sNeon = MakeNeon(); return sNeon;
                    case 4: if (sSteam == null) sSteam = MakeSteam(); return sSteam;
                    case 6: if (sVice == null) sVice = MakeVice(); return sVice;
                }
            }
            catch (Exception) { failed = true; }
            return null;
        }

        public static void Draw(Control c, Graphics g)
        {
            if (!On) return;
            Form f = c.FindForm();
            if (f == null) return;
            Bitmap sc = Scene();
            if (sc == null) return;
            GraphicsState st = g.Save();
            try
            {
                Point p = f.PointToClient(c.PointToScreen(Point.Empty));
                g.TranslateTransform(-p.X, -p.Y);
                Rectangle R = f.ClientRectangle;
                g.InterpolationMode = Xp.Theme == 4 ? InterpolationMode.HighQualityBilinear : InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                // proporcao original preservada: a imagem cobre a janela sem esticar (corta o excesso, centralizado)
                double k = Math.Max(R.Width / (double)sc.Width, R.Height / (double)sc.Height);
                int sw = Math.Min(sc.Width, (int)Math.Ceiling(R.Width / k)), sh = Math.Min(sc.Height, (int)Math.Ceiling(R.Height / k));
                g.DrawImage(sc, R, (sc.Width - sw) / 2, (sc.Height - sh) / 2, sw, sh, GraphicsUnit.Pixel);
                int va = Xp.Theme == 3 ? 212 : Xp.Theme == 6 ? 218 : 165;
                using (SolidBrush veil = new SolidBrush(A(va, Xp.Face.R, Xp.Face.G, Xp.Face.B))) g.FillRectangle(veil, R);
            }
            catch (Exception) { }
            g.Restore(st);
        }

        // ================= pixel art =================
        static readonly int[] Bay = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        static void Sky(Bitmap b, int y0, int y1, Color[] st, int levels)
        {
            for (int y = y0; y < y1; y++)
                for (int x = 0; x < b.Width; x++)
                {
                    double f = (y - y0) / (double)(y1 - y0) * levels;
                    int lo = (int)f;
                    if (f - lo > (Bay[(y & 3) * 4 + (x & 3)] + 0.5) / 16.0) lo++;
                    b.SetPixel(x, y, Ramp(st, Math.Min(1.0, lo / (double)levels)));
                }
        }
        static void Rc(Graphics g, Color c, int x, int y, int w, int h)
        {
            using (SolidBrush s = new SolidBrush(c)) g.FillRectangle(s, x, y, w, h);
        }
        static Graphics PxG(Bitmap b)
        {
            Graphics g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            return g;
        }
        static void Disc(Graphics g, int cx, int cy, int r, Color top, Color bot, int cutFrom, int cutStep)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                if (cutStep > 0 && dy > cutFrom && ((dy - cutFrom) % 8) < ((dy - cutFrom) / cutStep + 1)) continue;
                int dx = (int)Math.Sqrt(r * r - dy * dy + 0.0);
                Rc(g, Lp(top, bot, (dy + r) / (2.0 * r)), cx - dx, cy + dy, dx * 2 + 1, 1);
            }
        }
        // linha "pixelada" (sem suavizacao)
        static void PLine(Graphics g, Color c, int x0, int y0, int x1, int y1, int th)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy;
            using (SolidBrush s = new SolidBrush(c))
                while (true)
                {
                    g.FillRectangle(s, x0, y0, th, th);
                    if (x0 == x1 && y0 == y1) break;
                    int e2 = 2 * err;
                    if (e2 > -dy) { err -= dy; x0 += sx; }
                    if (e2 < dx) { err += dx; y0 += sy; }
                }
        }
        static void PalmPx(Graphics g, int bx, int by, int h, double lean, Color trunk, Color ring, Color leaf, Color leafHi)
        {
            int cx = bx, cy = by;
            for (int i = 0; i <= h; i++)
            {
                double u = i / (double)h;
                cx = bx + (int)(lean * u * u * h * 0.5);
                cy = by - i;
                int w = i < h / 5 ? 4 : 3;
                Rc(g, i % 6 == 0 ? ring : trunk, cx - w / 2, cy, w, 1);
            }
            double[] ang = { 8, 38, 66, 96, 126, 154, 178, -14, 200 };
            double L = h * 0.46;
            for (int k = 0; k < ang.Length; k++)
            {
                double a = ang[k] * Math.PI / 180;
                for (int s = 0; s <= (int)L; s++)
                {
                    double t = s / L;
                    int x = cx + (int)(Math.Cos(a) * L * t);
                    int y = cy - (int)(Math.Sin(a) * L * t) + (int)(L * 0.75 * t * t);
                    Rc(g, leaf, x, y, 2, 2);
                    if (s % 3 == 0 && s > 3)
                    {
                        int d = t < 0.6 ? 3 : 2;
                        Rc(g, leaf, x - d, y + d, 1, 2);
                        Rc(g, leaf, x + d, y + d, 1, 2);
                        Rc(g, leafHi, x, y - 1, 1, 1);
                    }
                }
            }
            Rc(g, leaf, cx - 2, cy - 1, 5, 4);
            Rc(g, ring, cx - 2, cy + 3, 2, 2); Rc(g, ring, cx + 1, cy + 3, 2, 2);
        }

        static Bitmap MakeNeon()
        {
            const int W = 360, H = 250, HZ = 168;
            Bitmap b = new Bitmap(W, H);
            Random r = new Random(7);
            Sky(b, 0, 150, new Color[] { Hex(0x07021a), Hex(0x1a0838), Hex(0x4a0f6e), Hex(0xb0188a), Hex(0xff3d9a) }, 14);
            Color[] wc = { Hex(0x2ef9ff), Hex(0xffd23f), Hex(0xff3da8), Hex(0x9d5cff) };
            using (Graphics g = PxG(b))
            {
                Rc(g, Hex(0xff3d9a), 0, 150, W, HZ - 150);
                for (int i = 0; i < 90; i++)
                {
                    int x = r.Next(W), y = r.Next(120);
                    Color c = r.Next(3) == 0 ? Hex(0x7df9ff) : Color.White;
                    Rc(g, A(255 - y * 2, c.R, c.G, c.B), x, y, 1, 1);
                    if (i % 10 == 0) { Rc(g, A(180, c.R, c.G, c.B), x - 1, y, 3, 1); Rc(g, A(180, c.R, c.G, c.B), x, y - 1, 1, 3); }
                }
                // sol de neon com listras
                Disc(g, 252, 120, 46, Hex(0xffe45c), Hex(0xff2e93), 6, 11);
                // predios ao fundo
                int xx = 0;
                while (xx < W)
                {
                    int w = r.Next(9, 22), h = r.Next(25, 80);
                    Rc(g, Hex(0x2a0b4e), xx, 152 - h, w, h);
                    Rc(g, Hex(0x3e1272), xx, 152 - h, w, 1);
                    for (int wy = 152 - h + 3; wy < 148; wy += 4)
                        for (int wx = xx + 2; wx < xx + w - 2; wx += 4)
                            if (r.Next(5) == 0) Rc(g, Hex(0x8a2aa8), wx, wy, 1, 2);
                    xx += w - (r.Next(3) == 0 ? 3 : 0);
                }
                // predios da frente com janelas e letreiros
                xx = -4;
                while (xx < W)
                {
                    int w = r.Next(14, 34), h = r.Next(50, 118), top = HZ - h;
                    Rc(g, Hex(0x0e0620), xx, top, w, h);
                    Rc(g, Hex(0x2b1257), xx, top, 1, h);
                    Rc(g, Hex(0x1c0c3a), xx, top, w, 1);
                    if (r.Next(3) == 0)
                    {
                        Rc(g, Hex(0x0e0620), xx + w / 2, top - 10, 1, 10);
                        Rc(g, Hex(0xff2040), xx + w / 2, top - 11, 1, 1);
                    }
                    for (int wy = top + 4; wy < HZ - 4; wy += 4)
                        for (int wx = xx + 3; wx < xx + w - 3; wx += 3)
                            if (r.Next(4) == 0) Rc(g, wc[r.Next(4)], wx, wy, 2, 2);
                    if (w >= 20 && r.Next(2) == 0)
                    {
                        Color sc = wc[r.Next(3)];
                        int sx = xx + w - 7, sy = top + 8;
                        Rc(g, A(55, sc.R, sc.G, sc.B), sx - 2, sy - 2, 9, 26);
                        Rc(g, sc, sx, sy, 5, 1); Rc(g, sc, sx, sy + 21, 5, 1); Rc(g, sc, sx, sy, 1, 22); Rc(g, sc, sx + 4, sy, 1, 22);
                        for (int q = 3; q < 20; q += 4) Rc(g, Color.White, sx + 2, sy + q, 1, 2);
                    }
                    xx += w + r.Next(0, 4);
                }
                // rua molhada
                Rc(g, Hex(0x12052a), 0, HZ, W, H - HZ);
                for (int y = HZ + 2; y < H; y += 3)
                {
                    int x1 = r.Next(W), len = r.Next(14, 70);
                    Color c = wc[r.Next(3)];
                    Rc(g, A(70, c.R, c.G, c.B), x1, y, len, 1);
                }
                using (Pen p = new Pen(HexA(190, 0xff2e93), 1f))
                {
                    for (int k = -16; k <= 16; k++) g.DrawLine(p, 180 + k * 3, HZ, 180 + k * 46, H);
                }
                using (Pen p = new Pen(HexA(170, 0x2ef9ff), 1f))
                {
                    for (int i = 1; i <= 9; i++)
                    {
                        int y = HZ + (int)(Math.Pow(i / 9.0, 2) * (H - HZ - 1));
                        g.DrawLine(p, 0, y, W, y);
                    }
                }
                // chuva
                for (int i = 0; i < 140; i++)
                {
                    int x = r.Next(W), y = r.Next(H - 8);
                    Rc(g, HexA(90, 0x9fe8ff), x, y, 1, 2);
                    Rc(g, HexA(60, 0x9fe8ff), x - 1, y + 2, 1, 2);
                }
            }
            return b;
        }

        static Bitmap MakeVice()
        {
            const int W = 360, H = 250, HZ = 142;
            Bitmap b = new Bitmap(W, H);
            Random r = new Random(11);
            Sky(b, 0, HZ, new Color[] { Hex(0x1a0b3d), Hex(0x5a1b7a), Hex(0xc4357e), Hex(0xff6a5c), Hex(0xffb347), Hex(0xffe29a) }, 18);
            Sky(b, HZ, 192, new Color[] { Hex(0xff8a6b), Hex(0xd23a86), Hex(0x6b1b8c), Hex(0x2a0f5c) }, 12);
            using (Graphics g = PxG(b))
            {
                for (int i = 0; i < 40; i++) { int x = r.Next(W), y = r.Next(40); Rc(g, A(230 - y * 4, 255, 255, 255), x, y, 1, 1); }
                // sol
                Disc(g, 180, 126, 48, Hex(0xfff3a0), Hex(0xff5e7e), 4, 9);
                Rc(g, Hex(0xffb347), 0, HZ - 1, W, 1);
                // nuvens em barras
                int[][] cl = { new int[] { 30, 60, 52, 3 }, new int[] { 260, 48, 60, 3 }, new int[] { 70, 84, 38, 2 }, new int[] { 230, 92, 46, 2 }, new int[] { 140, 40, 34, 2 }, new int[] { 300, 100, 40, 2 } };
                foreach (int[] q in cl)
                {
                    Rc(g, HexA(210, 0xff8fb8), q[0], q[1], q[2], q[3]);
                    Rc(g, HexA(210, 0xffd0e0), q[0] + 3, q[1] - 1, q[2] - 8, 1);
                    Rc(g, HexA(160, 0x5a1b7a), q[0] + 6, q[1] + q[3], q[2] - 10, 1);
                }
                // skyline art deco ao fundo
                Color[] pa = { Hex(0xff8fb8), Hex(0x4ee3d0), Hex(0xc9a0ff), Hex(0xffc0a0) };
                int xx = 4;
                while (xx < W - 8)
                {
                    int w = r.Next(10, 20), h = r.Next(14, 46);
                    Color c = Lp(pa[r.Next(4)], Hex(0x8a3a8a), 0.45);
                    Rc(g, c, xx, HZ - h, w, h);
                    Rc(g, Lp(c, Color.White, 0.35), xx, HZ - h, w, 1);
                    Rc(g, Lp(c, Color.Black, 0.3), xx + w - 2, HZ - h, 2, h);
                    if (r.Next(3) == 0) { Rc(g, c, xx + 2, HZ - h - 4, w - 4, 4); Rc(g, Lp(c, Color.White, 0.3), xx + 2, HZ - h - 4, w - 4, 1); }
                    for (int wy = HZ - h + 3; wy < HZ - 2; wy += 3)
                        for (int wx = xx + 2; wx < xx + w - 3; wx += 3)
                            if (r.Next(3) == 0) Rc(g, HexA(200, 0xfff3a0), wx, wy, 1, 1);
                    if (r.Next(6) == 0) { Rc(g, c, xx + w / 2, HZ - h - 14, 1, 14); }
                    xx += w + r.Next(0, 3);
                }
                // reflexo do sol no mar
                for (int y = HZ + 1; y < 190; y += 2)
                {
                    double t = (y - HZ) / 48.0;
                    int w = (int)(70 * (1 - t * 0.6)) + r.Next(-6, 7);
                    int off = r.Next(-3, 4);
                    Rc(g, A((int)(230 - t * 90), 255, 226, 150), 180 - w / 2 + off, y, Math.Max(4, w), 1);
                }
                for (int y = HZ + 4; y < 190; y += 5)
                    Rc(g, HexA(120, 0xffb0c8), r.Next(W), y, r.Next(6, 22), 1);
                // faixa de areia e rua
                Rc(g, Hex(0xf4b8a0), 0, 192, W, 6);
                Rc(g, Hex(0xd88a8a), 0, 196, W, 2);
                Rc(g, Hex(0x2a1546), 0, 198, W, H - 198);
                Rc(g, Hex(0xff3da8), 0, 198, W, 1);
                Rc(g, Hex(0x2ef9ff), 0, 199, W, 1);
                for (int x = 6; x < W; x += 34) Rc(g, Hex(0xfff3a0), x, 226, 18, 2);
                for (int x = 0; x < W; x += 5) Rc(g, Hex(0x3a1c60), x + (x / 5) % 2 * 2, 236 + (x / 5) % 3 * 3, 3, 1);
                // palmeiras (silhuetas)
                Color tr = Hex(0x1c0a33), rg = Hex(0x321452), lf = Hex(0x14072a), lh = Hex(0x4b1f6e);
                PalmPx(g, 38, 214, 132, -0.35, tr, rg, lf, lh);
                PalmPx(g, 96, 210, 84, 0.3, tr, rg, lf, lh);
                PalmPx(g, 324, 216, 150, 0.4, tr, rg, lf, lh);
                PalmPx(g, 276, 208, 78, -0.25, tr, rg, lf, lh);
                // conversivel na orla
                int cx = 196, cy = 212;
                Rc(g, HexA(110, 0x2ef9ff), cx - 2, cy + 11, 66, 2);
                Rc(g, Hex(0x1a0b2e), cx, cy + 3, 62, 8);
                Rc(g, Hex(0x1a0b2e), cx + 4, cy - 1, 18, 4); Rc(g, Hex(0x1a0b2e), cx + 20, cy - 3, 14, 6);
                Rc(g, Hex(0xff3da8), cx, cy + 6, 62, 1);
                Rc(g, Hex(0x7df9ff), cx + 22, cy - 2, 9, 3);
                Rc(g, Hex(0xff2040), cx - 1, cy + 4, 2, 2); Rc(g, Hex(0xfff3a0), cx + 61, cy + 5, 2, 2);
                foreach (int wx in new int[] { cx + 10, cx + 48 })
                {
                    Rc(g, Hex(0x0a0418), wx - 4, cy + 7, 9, 7); Rc(g, Hex(0x0a0418), wx - 3, cy + 6, 7, 9);
                    Rc(g, Hex(0x2ef9ff), wx - 1, cy + 9, 3, 3);
                }
            }
            return b;
        }

        // ================= Steampunk (arte realista, estatica) =================
        static Color Cu(double f)
        {
            return Ramp(new Color[] { Hex(0x3a1c08), Hex(0x8a4a1c), Hex(0xd98a4a), Hex(0xf6c79a), Hex(0xb5601f), Hex(0x5a2c0e), Hex(0x2a1306) }, f);
        }
        static void PipeLine(Graphics g, float x1, float y1, float x2, float y2, float th)
        {
            double dx = x2 - x1, dy = y2 - y1, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.5) return;
            float nx = (float)(-dy / len), ny = (float)(dx / len);
            int n = Math.Max(2, (int)th);
            for (int i = 0; i <= n; i++)
            {
                float o = i - th / 2f;
                using (Pen p = new Pen(Cu(i / (double)n), 1.7f)) g.DrawLine(p, x1 + nx * o, y1 + ny * o, x2 + nx * o, y2 + ny * o);
            }
        }
        static void PipeArc(Graphics g, float cx, float cy, float r, float a0, float sweep, float th)
        {
            int n = Math.Max(2, (int)th);
            for (int i = 0; i <= n; i++)
            {
                float rr = r + i - th / 2f;
                using (Pen p = new Pen(Cu(i / (double)n), 1.7f)) g.DrawArc(p, cx - rr, cy - rr, 2 * rr, 2 * rr, a0, sweep);
            }
        }
        static void Bolt(Graphics g, float x, float y, float r)
        {
            using (SolidBrush d = new SolidBrush(A(200, 30, 16, 4))) g.FillEllipse(d, x - r - 0.6f, y - r - 0.3f, 2 * r + 1.4f, 2 * r + 1.4f);
            RectangleF rc = new RectangleF(x - r, y - r, 2 * r, 2 * r);
            using (LinearGradientBrush lb = new LinearGradientBrush(rc, Hex(0xf5da92), Hex(0x6a4510), 45f)) g.FillEllipse(lb, rc);
            using (SolidBrush h = new SolidBrush(A(200, 255, 245, 210))) g.FillEllipse(h, x - r * 0.5f, y - r * 0.55f, r * 0.5f, r * 0.4f);
        }
        static void Collar(Graphics g, float cx, float cy, float len, float th, bool horiz)
        {
            float w = horiz ? len : th + 10, h = horiz ? th + 10 : len;
            RectangleF rc = new RectangleF(cx - w / 2, cy - h / 2, w, h);
            using (SolidBrush sh = new SolidBrush(A(90, 0, 0, 0))) g.FillRectangle(sh, rc.X + 3, rc.Y + 4, rc.Width, rc.Height);
            using (LinearGradientBrush lb = new LinearGradientBrush(rc, Hex(0xf1d487), Hex(0x6e4a12), horiz ? 90f : 0f))
            {
                ColorBlend cb = new ColorBlend(5);
                cb.Positions = new float[] { 0f, 0.2f, 0.45f, 0.75f, 1f };
                cb.Colors = new Color[] { Hex(0x7a5416), Hex(0xe9c46c), Hex(0xfbe9b0), Hex(0xb98a2a), Hex(0x4e330a) };
                lb.InterpolationColors = cb;
                g.FillRectangle(lb, rc);
            }
            using (Pen e = new Pen(A(230, 40, 22, 6), 1.2f)) g.DrawRectangle(e, rc.X, rc.Y, rc.Width, rc.Height);
            if (horiz) { Bolt(g, cx, rc.Y + 4, 2.4f); Bolt(g, cx, rc.Bottom - 4, 2.4f); }
            else { Bolt(g, rc.X + 4, cy, 2.4f); Bolt(g, rc.Right - 4, cy, 2.4f); }
        }
        static void Joint(Graphics g, float cx, float cy, float r)
        {
            using (SolidBrush sh = new SolidBrush(A(90, 0, 0, 0))) g.FillEllipse(sh, cx - r + 3, cy - r + 4, 2 * r, 2 * r);
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(cx - r, cy - r, 2 * r, 2 * r);
                using (PathGradientBrush pb = new PathGradientBrush(gp))
                {
                    pb.CenterColor = Hex(0xfbe9b0); pb.SurroundColors = new Color[] { Hex(0x5a3a0c) };
                    pb.CenterPoint = new PointF(cx - r * 0.35f, cy - r * 0.4f);
                    g.FillPath(pb, gp);
                }
            }
            using (Pen e = new Pen(A(220, 40, 22, 6), 1.2f)) g.DrawEllipse(e, cx - r, cy - r, 2 * r, 2 * r);
            for (int k = 0; k < 6; k++) Bolt(g, cx + (float)Math.Cos(k * Math.PI / 3) * r * 0.62f, cy + (float)Math.Sin(k * Math.PI / 3) * r * 0.62f, 1.8f);
        }
        static PointF[] GearPts(float cx, float cy, float r, int n, double ang)
        {
            PointF[] pts = new PointF[n * 4];
            double w = 2 * Math.PI / n;
            for (int i = 0; i < n; i++)
            {
                double a0 = ang + i * w;
                double[] aa = { a0, a0 + w * 0.16, a0 + w * 0.40, a0 + w * 0.56 };
                float[] rr = { r * 0.84f, r, r, r * 0.84f };
                for (int k = 0; k < 4; k++) pts[i * 4 + k] = new PointF(cx + (float)(Math.Cos(aa[k]) * rr[k]), cy + (float)(Math.Sin(aa[k]) * rr[k]));
            }
            return pts;
        }
        static void GearR(Graphics g, float cx, float cy, float r, int n, double rot, Color lite, Color dark)
        {
            PointF[] pts = GearPts(cx, cy, r, n, rot);
            PointF[] sh = new PointF[pts.Length];
            for (int i = 0; i < pts.Length; i++) sh[i] = new PointF(pts[i].X + r * 0.07f, pts[i].Y + r * 0.1f);
            using (SolidBrush sb = new SolidBrush(A(100, 0, 0, 0))) g.FillPolygon(sb, sh);
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddPolygon(pts);
                using (PathGradientBrush pb = new PathGradientBrush(gp))
                {
                    pb.CenterColor = lite; pb.SurroundColors = new Color[] { dark };
                    pb.CenterPoint = new PointF(cx - r * 0.3f, cy - r * 0.3f);
                    g.FillPath(pb, gp);
                }
            }
            using (Pen e = new Pen(A(230, 38, 20, 6), Math.Max(1.2f, r * 0.02f))) g.DrawPolygon(e, pts);
            float ri = r * 0.72f, rh = r * 0.2f;
            // vazados entre os raios
            using (SolidBrush hole = new SolidBrush(A(200, 22, 12, 4))) g.FillEllipse(hole, cx - ri, cy - ri, 2 * ri, 2 * ri);
            using (Pen p = new Pen(A(255, 120, 74, 24), r * 0.07f)) g.DrawEllipse(p, cx - ri, cy - ri, 2 * ri, 2 * ri);
            using (Pen p = new Pen(A(200, 245, 205, 130), 1.2f)) g.DrawEllipse(p, cx - ri - r * 0.03f, cy - ri - r * 0.03f, 2 * ri, 2 * ri);
            int spokes = n >= 10 ? 6 : 5;
            for (int k = 0; k < spokes; k++)
            {
                double a = rot + k * 2 * Math.PI / spokes;
                float x1 = cx + (float)Math.Cos(a) * rh, y1 = cy + (float)Math.Sin(a) * rh, x2 = cx + (float)Math.Cos(a) * ri, y2 = cy + (float)Math.Sin(a) * ri;
                using (Pen p = new Pen(A(255, 70, 40, 12), r * 0.15f)) g.DrawLine(p, x1 + 1.5f, y1 + 2f, x2 + 1.5f, y2 + 2f);
                using (Pen p = new Pen(Lp(dark, lite, 0.55), r * 0.12f)) g.DrawLine(p, x1, y1, x2, y2);
                using (Pen p = new Pen(A(190, 250, 225, 170), Math.Max(1f, r * 0.03f))) g.DrawLine(p, x1 - 1, y1 - 1, x2 - 1, y2 - 1);
            }
            using (GraphicsPath hp = new GraphicsPath())
            {
                hp.AddEllipse(cx - rh, cy - rh, 2 * rh, 2 * rh);
                using (PathGradientBrush pb = new PathGradientBrush(hp))
                {
                    pb.CenterColor = Hex(0xfbe9b0); pb.SurroundColors = new Color[] { Hex(0x6a4510) };
                    pb.CenterPoint = new PointF(cx - rh * 0.35f, cy - rh * 0.4f);
                    g.FillPath(pb, hp);
                }
            }
            using (Pen e = new Pen(A(230, 38, 20, 6), 1.2f)) g.DrawEllipse(e, cx - rh, cy - rh, 2 * rh, 2 * rh);
            Bolt(g, cx, cy, Math.Max(2.4f, rh * 0.38f));
        }
        static void GaugeR(Graphics g, float cx, float cy, float r, double v, string unit, bool nums)
        {
            using (SolidBrush sh = new SolidBrush(A(100, 0, 0, 0))) g.FillEllipse(sh, cx - r + 5, cy - r + 7, 2 * r, 2 * r);
            RectangleF rb = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);
            using (LinearGradientBrush lb = new LinearGradientBrush(rb, Hex(0xf6dc96), Hex(0x6b4410), 45f))
            {
                ColorBlend cb = new ColorBlend(4);
                cb.Positions = new float[] { 0f, 0.3f, 0.6f, 1f };
                cb.Colors = new Color[] { Hex(0xfbe9b0), Hex(0xc99a3c), Hex(0x7a5416), Hex(0xd8aa50) };
                lb.InterpolationColors = cb;
                g.FillEllipse(lb, rb);
            }
            using (Pen e = new Pen(A(230, 38, 20, 6), 1.5f)) g.DrawEllipse(e, rb.X, rb.Y, rb.Width, rb.Height);
            float r2 = r * 0.86f, r3 = r * 0.8f;
            using (SolidBrush d = new SolidBrush(Hex(0x2a1708))) g.FillEllipse(d, cx - r2, cy - r2, 2 * r2, 2 * r2);
            using (GraphicsPath fp = new GraphicsPath())
            {
                fp.AddEllipse(cx - r3, cy - r3, 2 * r3, 2 * r3);
                using (PathGradientBrush pb = new PathGradientBrush(fp))
                {
                    pb.CenterColor = Hex(0xf9efd0); pb.SurroundColors = new Color[] { Hex(0xc2a972) };
                    g.FillPath(pb, fp);
                }
            }
            using (Pen rz = new Pen(A(210, 176, 32, 22), r * 0.07f)) g.DrawArc(rz, cx - r * 0.7f, cy - r * 0.7f, r * 1.4f, r * 1.4f, 135 + 27 * 8, 54);
            for (int i = 0; i <= 40; i++)
            {
                double a = (135 + i * 6.75) * Math.PI / 180;
                bool major = i % 4 == 0;
                float r1 = r * 0.76f, rt = r * (major ? 0.62f : 0.69f);
                using (Pen tk = new Pen(A(240, 50, 28, 10), major ? Math.Max(1.4f, r * 0.03f) : 1f))
                    g.DrawLine(tk, cx + (float)(Math.Cos(a) * r1), cy + (float)(Math.Sin(a) * r1), cx + (float)(Math.Cos(a) * rt), cy + (float)(Math.Sin(a) * rt));
            }
            if (nums)
                using (Font f = new Font("Georgia", Math.Max(6f, r * 0.15f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush tb = new SolidBrush(Hex(0x3a200a)))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                    for (int i = 0; i <= 10; i += 2)
                    {
                        double a = (135 + i * 27) * Math.PI / 180;
                        g.DrawString((i * 10).ToString(), f, tb, cx + (float)(Math.Cos(a) * r * 0.48), cy + (float)(Math.Sin(a) * r * 0.48), sf);
                    }
                }
            using (Font f2 = new Font("Georgia", Math.Max(6f, r * 0.12f), FontStyle.Italic, GraphicsUnit.Pixel))
            using (SolidBrush tb2 = new SolidBrush(Hex(0x4a2c10)))
            using (StringFormat sf2 = new StringFormat())
            {
                sf2.Alignment = StringAlignment.Center; sf2.LineAlignment = StringAlignment.Center;
                g.DrawString(unit, f2, tb2, cx, cy + r * 0.3f, sf2);
            }
            double na = (135 + v * 270) * Math.PI / 180;
            float cs = (float)Math.Cos(na), sn = (float)Math.Sin(na);
            PointF tip = new PointF(cx + cs * r * 0.7f, cy + sn * r * 0.7f), tail = new PointF(cx - cs * r * 0.2f, cy - sn * r * 0.2f);
            PointF b1 = new PointF(cx - sn * r * 0.05f, cy + cs * r * 0.05f), b2 = new PointF(cx + sn * r * 0.05f, cy - cs * r * 0.05f);
            using (SolidBrush ns = new SolidBrush(A(110, 0, 0, 0))) g.FillPolygon(ns, new PointF[] { new PointF(tip.X + 2, tip.Y + 3), new PointF(b1.X + 2, b1.Y + 3), new PointF(tail.X + 2, tail.Y + 3), new PointF(b2.X + 2, b2.Y + 3) });
            using (SolidBrush nb = new SolidBrush(Hex(0x8a1a10))) g.FillPolygon(nb, new PointF[] { tip, b1, tail, b2 });
            using (GraphicsPath hp = new GraphicsPath())
            {
                float hr = r * 0.09f;
                hp.AddEllipse(cx - hr, cy - hr, 2 * hr, 2 * hr);
                using (PathGradientBrush pb = new PathGradientBrush(hp)) { pb.CenterColor = Hex(0xfbe9b0); pb.SurroundColors = new Color[] { Hex(0x6a4510) }; g.FillPath(pb, hp); }
            }
            using (LinearGradientBrush gl = new LinearGradientBrush(new RectangleF(cx - r * 0.72f, cy - r * 0.8f, r * 1.1f, r * 0.9f), A(120, 255, 255, 255), A(0, 255, 255, 255), 90f))
                g.FillEllipse(gl, cx - r * 0.72f, cy - r * 0.8f, r * 1.1f, r * 0.9f);
            for (int k = 0; k < 4; k++)
            {
                double a = (45 + 90 * k) * Math.PI / 180;
                Bolt(g, cx + (float)Math.Cos(a) * r * 0.93f, cy + (float)Math.Sin(a) * r * 0.93f, Math.Max(1.6f, r * 0.045f));
            }
        }
        static void Valve(Graphics g, float cx, float cy, float r)
        {
            using (SolidBrush sh = new SolidBrush(A(90, 0, 0, 0))) g.FillEllipse(sh, cx - r + 4, cy - r + 6, 2 * r, 2 * r);
            for (int k = 0; k < 3; k++)
            {
                double a = k * Math.PI / 3;
                float dx = (float)Math.Cos(a) * r, dy = (float)Math.Sin(a) * r;
                using (Pen p = new Pen(Hex(0x6a1610), r * 0.14f)) g.DrawLine(p, cx - dx, cy - dy, cx + dx, cy + dy);
                using (Pen p = new Pen(Hex(0xc03a28), r * 0.06f)) g.DrawLine(p, cx - dx - 1, cy - dy - 1, cx + dx - 1, cy + dy - 1);
            }
            using (Pen p = new Pen(Hex(0x6a1610), r * 0.17f)) g.DrawEllipse(p, cx - r, cy - r, 2 * r, 2 * r);
            using (Pen p = new Pen(Hex(0xd24a34), r * 0.07f)) g.DrawEllipse(p, cx - r - 1, cy - r - 1, 2 * r, 2 * r);
            Bolt(g, cx, cy, r * 0.16f);
        }
        static void Puff(Graphics g, float cx, float cy, float r, int a)
        {
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(cx - r, cy - r, 2 * r, 2 * r);
                using (PathGradientBrush pb = new PathGradientBrush(gp))
                {
                    pb.CenterColor = A(a, 255, 250, 240); pb.SurroundColors = new Color[] { A(0, 255, 250, 240) };
                    g.FillPath(pb, gp);
                }
            }
        }

        // tubos usados como divisorias dos menus (tema Steampunk)
        public static void HPipe(Graphics g, float x1, float x2, float y, float th)
        {
            PipeLine(g, x1, y, x2, y, th);
            Collar(g, x1 + 3, y, 7, th, false);
            Collar(g, x2 - 3, y, 7, th, false);
            Collar(g, (x1 + x2) / 2, y, 8, th, false);
        }
        public static void VPipe(Graphics g, float x, float y1, float y2, float th)
        {
            PipeLine(g, x, y1, x, y2, th);
            for (float y = y1 + 40; y < y2 - 10; y += 150) Collar(g, x, y, 7, th, true);
        }
        public static void GroupPipe(Graphics g, int w, int h)
        {
            using (GraphicsPath p = Xp.Round(new Rectangle(2, 10, w - 5, h - 13), 5))
            {
                using (Pen d = new Pen(A(255, 52, 26, 8), 5f)) g.DrawPath(d, p);
                using (Pen m = new Pen(Hex(0xb5601f), 3.2f)) g.DrawPath(m, p);
                g.TranslateTransform(-0.8f, -0.8f);
                using (Pen l = new Pen(A(210, 246, 199, 154), 1f)) g.DrawPath(l, p);
                g.TranslateTransform(0.8f, 0.8f);
            }
            Bolt(g, 3, 11, 2.6f); Bolt(g, w - 3, 11, 2.6f); Bolt(g, 3, h - 4, 2.6f); Bolt(g, w - 3, h - 4, 2.6f);
        }

        static Bitmap MakeSteam()
        {
            const int W = 1000, H = 700;
            Bitmap b = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
            Random r = new Random(5);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                // parede de madeira escura com rebites
                using (LinearGradientBrush wall = new LinearGradientBrush(new Rectangle(0, 0, W, H), Hex(0x3b2515), Hex(0x1e1209), 90f)) g.FillRectangle(wall, 0, 0, W, H);
                for (int i = 0; i < 700; i++)
                {
                    int x = r.Next(W), y = r.Next(H), l = r.Next(20, 120);
                    using (Pen p = new Pen(A(r.Next(8, 22), r.Next(2) == 0 ? 0 : 255, r.Next(2) == 0 ? 0 : 200, 120), 1f)) g.DrawLine(p, x, y, x + l, y);
                }
                for (int x = 0; x < W; x += 96)
                {
                    using (Pen p = new Pen(A(120, 0, 0, 0), 3f)) g.DrawLine(p, x, 0, x, H);
                    using (Pen p = new Pen(A(40, 255, 210, 150), 1f)) g.DrawLine(p, x + 2, 0, x + 2, H);
                    for (int y = 40; y < H; y += 120) Bolt(g, x + 48, y, 3.2f);
                }
                Color gl = Hex(0xd7a75a), gd = Hex(0x4a2c0c);
                // engrenagens de fundo
                GearR(g, 90, 112, 78, 14, 0.2, gl, gd);
                GearR(g, 210, 190, 50, 10, 0.55, gl, gd);
                GearR(g, 862, 98, 70, 13, 0.1, gl, gd);
                GearR(g, 936, 212, 42, 9, 0.4, gl, gd);
                GearR(g, 60, 546, 66, 12, 0.3, gl, gd);
                GearR(g, 160, 630, 40, 9, 0.7, gl, gd);
                GearR(g, 500, 650, 56, 11, 0.15, gl, gd);
                // tubulacao
                PipeLine(g, -10, 49, W + 10, 49, 16);
                for (int x = 70; x < W; x += 230) Collar(g, x, 49, 12, 16, false);
                PipeLine(g, -10, 294, W + 10, 294, 28);
                for (int x = 60; x < W; x += 210) Collar(g, x, 294, 14, 28, false);
                PipeLine(g, 620, 294, 620, H + 10, 24);
                Collar(g, 620, 430, 12, 24, true); Collar(g, 620, 575, 12, 24, true);
                PipeLine(g, 140, -10, 140, 294, 20);
                Collar(g, 140, 120, 12, 20, true); Collar(g, 140, 220, 12, 20, true);
                Joint(g, 140, 294, 24); Joint(g, 620, 294, 26);
                PipeLine(g, -10, 640, 380, 640, 18);
                PipeArc(g, 380, 600, 40, 0, 90, 18);
                PipeLine(g, 420, 600, 420, 470, 18);
                Collar(g, 420, 470, 12, 18, true); Collar(g, 120, 640, 12, 18, false); Collar(g, 260, 640, 12, 18, false);
                // verdigris nas juntas
                for (int i = 0; i < 70; i++)
                {
                    float x = 40 + r.Next(W - 80), y = i % 2 == 0 ? 294 + r.Next(-12, 13) : 49 + r.Next(-6, 7);
                    using (SolidBrush v = new SolidBrush(A(r.Next(25, 55), 70, 170, 140))) g.FillEllipse(v, x, y, r.Next(3, 9), r.Next(2, 5));
                }
                // manometros ligados por derivacoes
                PipeLine(g, 720, 300, 720, 356, 10); GaugeR(g, 720, 408, 52, 0.62, "PSI", true);
                PipeLine(g, 300, 640, 300, 566, 10); GaugeR(g, 300, 520, 44, 0.35, "BAR", true);
                PipeLine(g, 100, 300, 100, 330, 9); GaugeR(g, 100, 366, 36, 0.78, "TEMP", false);
                PipeLine(g, 500, 56, 500, 66, 9); GaugeR(g, 500, 108, 42, 0.5, "VAC", true);
                // valvula com vapor
                PipeLine(g, 860, 290, 860, 252, 8); Valve(g, 860, 232, 30);
                Puff(g, 905, 200, 30, 70); Puff(g, 925, 160, 42, 55); Puff(g, 945, 112, 56, 40); Puff(g, 960, 56, 70, 28);
                PipeLine(g, 380, 56, 380, 80, 8); Valve(g, 380, 104, 22);
                // engrenagens da frente
                GearR(g, 880, 590, 104, 16, 0.05, gl, gd);
                GearR(g, 745, 660, 46, 10, 0.5, gl, gd);
                // vinheta
                using (GraphicsPath vp = new GraphicsPath())
                {
                    vp.AddRectangle(new Rectangle(0, 0, W, H));
                    using (PathGradientBrush pb = new PathGradientBrush(vp))
                    {
                        pb.CenterColor = A(0, 0, 0, 0); pb.SurroundColors = new Color[] { A(150, 0, 0, 0) };
                        pb.FocusScales = new PointF(0.6f, 0.6f);
                        g.FillRectangle(pb, 0, 0, W, H);
                    }
                }
            }
            return b;
        }
    }

    // ------------------------------------------------------------------
    //  Render OpenGL real (carga da GPU): pelos em um toro, estilo FurMark
    // ------------------------------------------------------------------
    class GlCanvas : Control
    {
        [StructLayout(LayoutKind.Sequential)]
        struct PFD
        {
            public ushort nSize, nVersion; public uint dwFlags;
            public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift, cAlphaBits, cAlphaShift, cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits, cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved;
            public uint dwLayerMask, dwVisibleMask, dwDamageMask;
        }
        [DllImport("gdi32.dll")] static extern int ChoosePixelFormat(IntPtr hdc, ref PFD pfd);
        [DllImport("gdi32.dll")] static extern bool SetPixelFormat(IntPtr hdc, int fmt, ref PFD pfd);
        [DllImport("gdi32.dll")] static extern bool SwapBuffers(IntPtr hdc);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        const string GL = "opengl32.dll";
        [DllImport(GL)] static extern IntPtr wglCreateContext(IntPtr hdc);
        [DllImport(GL)] static extern bool wglMakeCurrent(IntPtr hdc, IntPtr rc);
        [DllImport(GL)] static extern bool wglDeleteContext(IntPtr rc);
        [DllImport(GL)] static extern IntPtr wglGetProcAddress(string n);
        [DllImport(GL)] static extern void glClearColor(float r, float g, float b, float a);
        [DllImport(GL)] static extern void glClear(uint m);
        [DllImport(GL)] static extern void glViewport(int x, int y, int w, int h);
        [DllImport(GL)] static extern void glMatrixMode(uint m);
        [DllImport(GL)] static extern void glLoadIdentity();
        [DllImport(GL)] static extern void glFrustum(double l, double r, double b, double t, double n, double f);
        [DllImport(GL)] static extern void glTranslatef(float x, float y, float z);
        [DllImport(GL)] static extern void glRotatef(float a, float x, float y, float z);
        [DllImport(GL)] static extern void glEnable(uint c);
        [DllImport(GL)] static extern void glDisable(uint c);
        [DllImport(GL)] static extern void glBlendFunc(uint s, uint d);
        [DllImport(GL)] static extern void glLineWidth(float w);
        [DllImport(GL)] static extern void glEnableClientState(uint c);
        [DllImport(GL)] static extern void glDisableClientState(uint c);
        [DllImport(GL)] static extern void glVertexPointer(int size, uint type, int stride, IntPtr p);
        [DllImport(GL)] static extern void glColorPointer(int size, uint type, int stride, IntPtr p);
        [DllImport(GL)] static extern void glDrawArrays(uint mode, int first, int count);
        [DllImport(GL)] static extern void glBegin(uint m);
        [DllImport(GL)] static extern void glEnd();
        [DllImport(GL)] static extern void glVertex2f(float x, float y);
        [DllImport(GL)] static extern IntPtr glGetString(uint n);
        [DllImport(GL)] static extern void glPushMatrix();
        [DllImport(GL)] static extern void glPopMatrix();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint DCreateShader(uint type);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DShaderSource(uint s, int count, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] src, IntPtr len);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DCompile(uint s);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DGetiv(uint s, uint pname, out int p);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint DCreateProgram();
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DAttach(uint p, uint s);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DLink(uint p);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DUse(uint p);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DGetUniform(uint p, [MarshalAs(UnmanagedType.LPStr)] string name);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DUniform1f(int loc, float v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DUniform2f(int loc, float a, float b);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DSwapInterval(int i);

        public volatile int State; // 0 iniciando, 1 ok, 2 falhou
        public string Error = "", Renderer = "";
        volatile bool quit;
        public void StopRender() { quit = true; } volatile int vw = 640, vh = 400;
        Th.Thread th;

        public GlCanvas()
        {
            SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Black;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnResize(EventArgs e) { base.OnResize(e); vw = Math.Max(1, Width); vh = Math.Max(1, Height); }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (th != null) return;
            vw = Math.Max(1, Width); vh = Math.Max(1, Height);
            IntPtr h = Handle;
            th = new Th.Thread(delegate() { Run(h); });
            th.IsBackground = true; th.Name = "GL"; th.Start();
        }
        protected override void Dispose(bool d)
        {
            quit = true;
            if (th != null && th.IsAlive) th.Join(3000);
            base.Dispose(d);
        }

        T Fn<T>(string name) where T : class
        {
            IntPtr p = wglGetProcAddress(name);
            if (p == IntPtr.Zero || p == (IntPtr)1 || p == (IntPtr)2 || p == (IntPtr)3 || p == (IntPtr)(-1)) return null;
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        const string FS =
            "uniform float t; uniform vec2 res; uniform float it;\n" +
            "void main(){ vec2 p=(gl_FragCoord.xy-0.5*res)/res.y; vec2 q=p*3.0; float a=0.0;\n" +
            "for(int i=0;i<600;i++){ if(float(i)>=it) break; q=vec2(sin(q.y*1.7+t)+q.x*0.3, cos(q.x*1.3-t)-q.y*0.3); a+=length(q)*0.01; }\n" +
            "vec3 c=0.5+0.5*sin(a*5.0+vec3(0.0,2.0,4.0)); gl_FragColor=vec4(c*0.32,1.0); }";

        void Run(IntPtr hwnd)
        {
            IntPtr hdc = IntPtr.Zero, rc = IntPtr.Zero;
            GCHandle hv = default(GCHandle), hc = default(GCHandle);
            try
            {
                hdc = GetDC(hwnd);
                PFD pfd = new PFD();
                pfd.nSize = (ushort)Marshal.SizeOf(typeof(PFD)); pfd.nVersion = 1; pfd.dwFlags = 4 | 0x20 | 1; pfd.iPixelType = 0; pfd.cColorBits = 32; pfd.cDepthBits = 24;
                int fmt = ChoosePixelFormat(hdc, ref pfd);
                if (fmt == 0 || !SetPixelFormat(hdc, fmt, ref pfd)) throw new Exception("formato de pixel OpenGL indisponivel");
                rc = wglCreateContext(hdc);
                if (rc == IntPtr.Zero || !wglMakeCurrent(hdc, rc)) throw new Exception("nao foi possivel criar o contexto OpenGL");
                string rend = "";
                try { rend = Marshal.PtrToStringAnsi(glGetString(0x1F01)); } catch (Exception) { }
                DSwapInterval swap = null;
                try { swap = Fn<DSwapInterval>("wglSwapIntervalEXT"); if (swap != null) swap(0); } catch (Exception) { }

                // shader de fragmento pesado (carga de ALU)
                uint prog = 0; int lT = -1, lRes = -1, lIt = -1;
                DUse use = null; DUniform1f u1 = null; DUniform2f u2 = null;
                try
                {
                    DCreateShader cs = Fn<DCreateShader>("glCreateShader"); DShaderSource ss = Fn<DShaderSource>("glShaderSource"); DCompile co = Fn<DCompile>("glCompileShader");
                    DGetiv gsi = Fn<DGetiv>("glGetShaderiv"), gpi = Fn<DGetiv>("glGetProgramiv");
                    DCreateProgram cp = Fn<DCreateProgram>("glCreateProgram"); DAttach at = Fn<DAttach>("glAttachShader"); DLink lk = Fn<DLink>("glLinkProgram");
                    DGetUniform gu = Fn<DGetUniform>("glGetUniformLocation");
                    use = Fn<DUse>("glUseProgram"); u1 = Fn<DUniform1f>("glUniform1f"); u2 = Fn<DUniform2f>("glUniform2f");
                    if (cs != null && ss != null && co != null && cp != null && at != null && lk != null && gu != null && use != null && u1 != null && u2 != null && gsi != null && gpi != null)
                    {
                        uint sh = cs(0x8B30);
                        ss(sh, 1, new string[] { FS }, IntPtr.Zero); co(sh);
                        int ok; gsi(sh, 0x8B81, out ok);
                        if (ok != 0)
                        {
                            uint pr = cp(); at(pr, sh); lk(pr);
                            gpi(pr, 0x8B82, out ok);
                            if (ok != 0) { prog = pr; lT = gu(pr, "t"); lRes = gu(pr, "res"); lIt = gu(pr, "it"); }
                        }
                    }
                }
                catch (Exception) { prog = 0; }

                // pelos: linhas sobre um toro
                int N = 90000;
                float[] pos = new float[N * 6], col = new float[N * 8];
                Random rnd = new Random(3);
                for (int i = 0; i < N; i++)
                {
                    double u = rnd.NextDouble() * Math.PI * 2, v = rnd.NextDouble() * Math.PI * 2;
                    double R0 = 1.0, r0 = 0.45;
                    double nx = Math.Cos(v) * Math.Cos(u), ny = Math.Sin(v), nz = Math.Cos(v) * Math.Sin(u);
                    double bx = (R0 + r0 * Math.Cos(v)) * Math.Cos(u), by = r0 * Math.Sin(v), bz = (R0 + r0 * Math.Cos(v)) * Math.Sin(u);
                    double len = 0.16 + rnd.NextDouble() * 0.2;
                    double ex = bx + nx * len + (rnd.NextDouble() - 0.5) * 0.06, ey = by + ny * len - 0.04, ez = bz + nz * len + (rnd.NextDouble() - 0.5) * 0.06;
                    pos[i * 6] = (float)bx; pos[i * 6 + 1] = (float)by; pos[i * 6 + 2] = (float)bz;
                    pos[i * 6 + 3] = (float)ex; pos[i * 6 + 4] = (float)ey; pos[i * 6 + 5] = (float)ez;
                    col[i * 8] = 0.45f; col[i * 8 + 1] = 0.08f; col[i * 8 + 2] = 0.0f; col[i * 8 + 3] = 0.5f;
                    col[i * 8 + 4] = 1.0f; col[i * 8 + 5] = 0.75f + (float)rnd.NextDouble() * 0.2f; col[i * 8 + 6] = 0.1f; col[i * 8 + 7] = 0.5f;
                }
                hv = GCHandle.Alloc(pos, GCHandleType.Pinned); hc = GCHandle.Alloc(col, GCHandleType.Pinned);
                Renderer = rend ?? "";
                Bench.Extra = rend + (prog != 0 ? "  |  shader + pelos OpenGL" : "  |  pelos OpenGL");
                State = 1;
                DateTime t0 = DateTime.UtcNow;
                while (!quit && !Bench.Stop)
                {
                    float t = (float)(DateTime.UtcNow - t0).TotalSeconds;
                    int w = vw, h = vh;
                    glViewport(0, 0, w, h);
                    glClearColor(0.02f, 0.02f, 0.03f, 1f);
                    glClear(0x4000 | 0x100);
                    glDisable(0xB71); glDisable(0xBE2);
                    if (prog != 0)
                    {
                        glMatrixMode(0x1701); glLoadIdentity(); glMatrixMode(0x1700); glLoadIdentity();
                        use(prog); u1(lT, t); u2(lRes, w, h); u1(lIt, 500f);
                        glBegin(7); glVertex2f(-1, -1); glVertex2f(1, -1); glVertex2f(1, 1); glVertex2f(-1, 1); glEnd();
                        use(0);
                    }
                    double asp = w / (double)h;
                    glMatrixMode(0x1701); glLoadIdentity(); glFrustum(-asp * 0.1, asp * 0.1, -0.1, 0.1, 0.2, 50);
                    glMatrixMode(0x1700); glLoadIdentity();
                    glTranslatef(0, 0, -3.4f); glRotatef(30f + 12f * (float)Math.Sin(t * 0.4), 1, 0, 0); glRotatef(t * 35f, 0, 1, 0);
                    glEnable(0xBE2); glBlendFunc(0x302, 1);
                    glEnableClientState(0x8074); glEnableClientState(0x8076);
                    glVertexPointer(3, 0x1406, 0, hv.AddrOfPinnedObject());
                    glColorPointer(4, 0x1406, 0, hc.AddrOfPinnedObject());
                    for (int p = 0; p < 4; p++)
                    {
                        glPushMatrix(); glRotatef(p * 0.35f, 0, 1, 0);
                        glDrawArrays(1, 0, N * 2);
                        glPopMatrix();
                    }
                    glDisableClientState(0x8074); glDisableClientState(0x8076);
                    SwapBuffers(hdc);
                    Th.Interlocked.Increment(ref Bench.Counter);
                }
            }
            catch (Exception ex) { Error = ex.Message; State = 2; }
            finally
            {
                try { if (hv.IsAllocated) hv.Free(); if (hc.IsAllocated) hc.Free(); } catch (Exception) { }
                try { wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); if (rc != IntPtr.Zero) wglDeleteContext(rc); if (hdc != IntPtr.Zero) ReleaseDC(hwnd, hdc); } catch (Exception) { }
            }
        }
    }


    static class Tpu
    {
        public static string Query(string name, bool gpu)
        {
            string n = name ?? "";
            n = Regex.Replace(n, @"\((R|TM)\)", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"@.*$", "");
            n = Regex.Replace(n, @"\bwith\b.*$", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\b\d+(st|nd|rd|th) Gen\b", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\b\d+-Core\b", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\b(NVIDIA|GeForce|AMD|Intel|Corporation|Graphics|Processor|CPU|Series)\b", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\s+", " ").Trim();
            return n;
        }
        public static void Open(string name, bool gpu, IWin32Window owner)
        {
            string q = Query(name, gpu);
            if (q.Length == 0) { Dlg.Show(owner, "Nome do hardware nao identificado.", "BunoTunner"); return; }
            string url = "https://www.techpowerup.com/" + (gpu ? "gpu-specs/" : "cpu-specs/") + "?q=" + Uri.EscapeDataString(q);
            try { ProcessStartInfo psi = new ProcessStartInfo(url); psi.UseShellExecute = true; Process.Start(psi); }
            catch (Exception ex) { Dlg.Show(owner, "Nao foi possivel abrir o navegador: " + ex.Message, "BunoTunner"); }
        }
    }

    // ------------------------------------------------------------------
    //  Motor dos testes de estresse (GPU, CPU, FPU, RAM, SSD)
    // ------------------------------------------------------------------
    static class Bench
    {
        public static volatile bool Stop;
        static readonly string[] VendorWords = { "nvidia", "amd", "intel", "corporation", "graphics", "series", "generation", "laptop", "gpu", "pcie", "sse2", "microsoft" };
        static List<string> NameTokens(string s)
        {
            List<string> l = new List<string>();
            if (string.IsNullOrEmpty(s)) return l;
            int sl = s.IndexOf('/'); if (sl > 0) s = s.Substring(0, sl);
            foreach (string w in Regex.Split(s.ToLower(), "[^a-z0-9]+"))
                if (w.Length >= 2 && Array.IndexOf(VendorWords, w) < 0 && w != "r" && w != "tm") l.Add(w);
            return l;
        }
        // compara o nome da GPU (WMI/NVML) com o do OpenGL/OpenCL, tolerando diferencas de formato
        public static bool GpuNameMatch(string a, string b)
        {
            List<string> ta = NameTokens(a), tb = NameTokens(b);
            if (ta.Count == 0 || tb.Count == 0) return false;
            List<string> small = ta.Count <= tb.Count ? ta : tb, big = ta.Count <= tb.Count ? tb : ta;
            int hit = 0;
            foreach (string w in small) if (big.Contains(w)) hit++;
            return hit == small.Count || (hit >= 2 && hit * 10 >= small.Count * 8);
        }
        public static long Counter, Errors;
        public static volatile string Extra = "", Fail;
        static int alive;
        public static bool Alive { get { return Th.Interlocked.CompareExchange(ref alive, 0, 0) > 0; } }

        [StructLayout(LayoutKind.Sequential)]
        struct MemStatus
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemStatus m);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(IntPtr h, IntPtr buf, uint n, out uint read, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(IntPtr h, IntPtr buf, uint n, out uint written, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetFilePointerEx(IntPtr h, long dist, out long newPos, uint method);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        public static long AvailPhys()
        {
            MemStatus m = new MemStatus();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MemStatus));
            return GlobalMemoryStatusEx(ref m) ? (long)m.ullAvailPhys : 0;
        }

        static void Spawn(Th.ParameterizedThreadStart fn, object arg, string name)
        {
            Th.Interlocked.Increment(ref alive);
            Th.Thread t = new Th.Thread(delegate(object o)
            {
                try { fn(o); }
                catch (Exception ex) { Fail = name + ": " + ex.Message; }
                finally { Th.Interlocked.Decrement(ref alive); }
            });
            t.IsBackground = true; t.Priority = Th.ThreadPriority.BelowNormal; t.Name = name;
            t.Start(arg);
        }

        // kind: 0 GPU, 1 CPU, 2 FPU, 3 RAM, 4 SSD
        public static void Start(int kind, int threads, int ramPct, string drive, int ssdMb, string gpuName)
        {
            Stop = false; Counter = 0; Errors = 0; Extra = ""; Fail = null;
            switch (kind)
            {
                case 0:
                    Spawn(delegate(object o) { GpuRun((string)o); }, gpuName, "bench-gpu");
                    break;
                case 1:
                    for (int i = 0; i < threads; i++) Spawn(CpuWorker, i, "bench-cpu");
                    break;
                case 2:
                    for (int i = 0; i < threads; i++) Spawn(FpuWorker, i, "bench-fpu");
                    break;
                case 3:
                    {
                        long avail = AvailPhys();
                        long total = Math.Min(avail * ramPct / 100, avail - 1024L * 1048576L);
                        if (total < 128L * 1048576L) { Fail = "Memoria livre insuficiente para o teste."; break; }
                        int th = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));
                        long per = total / th;
                        for (int i = 0; i < th; i++) Spawn(delegate(object o) { RamWorker(per); }, i, "bench-ram");
                        Extra = "Usando " + (total >> 20) + " MB em " + th + " thread(s)";
                        break;
                    }
                case 4:
                    Spawn(delegate(object o) { SsdRun(drive, ssdMb); }, null, "bench-ssd");
                    break;
            }
        }

        // ---------- CPU (inteiros) ----------
        [MethodImpl(MethodImplOptions.NoInlining)]
        static ulong IntKernel(ulong x, int n)
        {
            ulong acc = 0;
            for (int i = 0; i < n; i++)
            {
                x ^= x << 13; x ^= x >> 7; x ^= x << 17;
                acc += x * 0x9E3779B97F4A7C15UL;
                acc = (acc << 5) | (acc >> 59);
            }
            return acc ^ x;
        }
        static void CpuWorker(object o)
        {
            const int N = 1500000;
            ulong seed = 0x123456789ABCDEFUL;
            ulong refv = IntKernel(seed, N);
            while (!Stop)
            {
                if (IntKernel(seed, N) != refv) Th.Interlocked.Increment(ref Errors);
                Th.Interlocked.Add(ref Counter, (long)N * 10);
            }
        }

        // ---------- FPU (matrizes de double) ----------
        [MethodImpl(MethodImplOptions.NoInlining)]
        static double Mat(double[] a, double[] b, double[] c, int n)
        {
            Array.Clear(c, 0, c.Length);
            for (int i = 0; i < n; i++)
                for (int k = 0; k < n; k++)
                {
                    double aik = a[i * n + k];
                    int bi = k * n, ci = i * n;
                    for (int j = 0; j < n; j++) c[ci + j] += aik * b[bi + j];
                }
            double s = 0;
            for (int i = 0; i < c.Length; i++) s += c[i];
            return s;
        }
        static void FpuWorker(object o)
        {
            const int M = 96;
            double[] a = new double[M * M], b = new double[M * M], c = new double[M * M];
            for (int i = 0; i < a.Length; i++) { a[i] = ((i * 37) % 101) / 101.0 + 0.5; b[i] = ((i * 53) % 89) / 89.0 + 0.25; }
            double refv = Mat(a, b, c, M);
            while (!Stop)
            {
                if (Mat(a, b, c, M) != refv) Th.Interlocked.Increment(ref Errors);
                Th.Interlocked.Add(ref Counter, 2L * M * M * M);
            }
        }

        // ---------- RAM ----------
        static void RamWorker(long bytes)
        {
            int chunk = 8 * 1024 * 1024;   // 64 MB por bloco
            int n = (int)Math.Max(1, bytes / (chunk * 8L));
            List<long[]> bufs = new List<long[]>();
            for (int i = 0; i < n && !Stop; i++) bufs.Add(new long[chunk]);
            ulong[] pats = { 0xAAAAAAAAAAAAAAAAUL, 0x5555555555555555UL, 0xFFFFFFFF00000000UL, 0x00000000FFFFFFFFUL };
            int round = 0;
            while (!Stop)
            {
                long p = (long)pats[round++ & 3];
                foreach (long[] b in bufs)
                {
                    if (Stop) break;
                    for (int i = 0; i < b.Length; i++) b[i] = p ^ i;
                    Th.Interlocked.Add(ref Counter, (long)b.Length * 8);
                    long bad = 0;
                    for (int i = 0; i < b.Length; i++) if (b[i] != (p ^ i)) bad++;
                    if (bad > 0) Th.Interlocked.Add(ref Errors, bad);
                    Th.Interlocked.Add(ref Counter, (long)b.Length * 8);
                }
            }
        }

        // ---------- SSD (E/S sem cache do Windows, com verificacao) ----------
        static void SsdRun(string drive, int mb)
        {
            string path = Path.Combine(drive, "BunoTunner_bench.tmp");
            try
            {
                DriveInfo di = new DriveInfo(drive.Substring(0, 1));
                if (di.AvailableFreeSpace < (long)mb * 1048576L + 512L * 1048576L) { Fail = "Pouco espaco livre no drive " + drive; return; }
            }
            catch (Exception) { }
            const uint RW = 0xC0000000u;
            const uint FLAGS = 0x20000000u | 0x80000000u | 0x04000000u;   // NO_BUFFERING | WRITE_THROUGH | DELETE_ON_CLOSE
            IntPtr h = CreateFile(path, RW, 0, IntPtr.Zero, 2, FLAGS, IntPtr.Zero);
            if (h == new IntPtr(-1)) { Fail = "Nao consegui criar o arquivo de teste em " + drive + " (erro " + Marshal.GetLastWin32Error() + ")"; return; }
            const int BLK = 1 << 20;
            IntPtr raw = Marshal.AllocHGlobal(BLK + 4096);
            try
            {
                IntPtr buf = new IntPtr((raw.ToInt64() + 4095) & ~4095L);
                byte[] rnd = new byte[BLK];
                new Random(12345).NextBytes(rnd);
                Marshal.Copy(rnd, 0, buf, BLK);
                Random rand = new Random();
                double w = 0, rd = 0, iops = 0;
                long np; uint done;
                while (!Stop)
                {
                    SetFilePointerEx(h, 0, out np, 0);
                    Stopwatch sw = Stopwatch.StartNew();
                    int wrote = 0;
                    for (int i = 0; i < mb && !Stop; i++)
                    {
                        Marshal.WriteInt32(buf, 0, i); Marshal.WriteInt32(buf, 4, ~i);
                        if (!WriteFile(h, buf, BLK, out done, IntPtr.Zero)) { Fail = "Erro de escrita (" + Marshal.GetLastWin32Error() + ")"; return; }
                        Th.Interlocked.Add(ref Counter, done); wrote++;
                    }
                    w = wrote / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                    Extra = "Escrita seq " + w.ToString("0") + " MB/s";
                    if (Stop) break;

                    SetFilePointerEx(h, 0, out np, 0);
                    sw.Restart();
                    for (int i = 0; i < mb && !Stop; i++)
                    {
                        if (!ReadFile(h, buf, BLK, out done, IntPtr.Zero)) { Fail = "Erro de leitura (" + Marshal.GetLastWin32Error() + ")"; return; }
                        if (Marshal.ReadInt32(buf, 0) != i || Marshal.ReadInt32(buf, 4) != ~i) Th.Interlocked.Increment(ref Errors);
                        Th.Interlocked.Add(ref Counter, done);
                    }
                    rd = mb / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                    Extra = "Escrita seq " + w.ToString("0") + " MB/s | Leitura seq " + rd.ToString("0") + " MB/s";
                    if (Stop) break;

                    sw.Restart();
                    int ops = 0;
                    while (sw.ElapsedMilliseconds < 3000 && !Stop)
                    {
                        long off = (long)rand.Next(0, mb * 256) * 4096L;
                        SetFilePointerEx(h, off, out np, 0);
                        if (ReadFile(h, buf, 4096, out done, IntPtr.Zero)) { ops++; Th.Interlocked.Add(ref Counter, done); }
                    }
                    iops = ops / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                    Extra = "Escrita seq " + w.ToString("0") + " MB/s | Leitura seq " + rd.ToString("0") + " MB/s | 4K aleatorio " + iops.ToString("0") + " IOPS";
                }
            }
            finally { Marshal.FreeHGlobal(raw); CloseHandle(h); }
        }

        // ---------- GPU (OpenCL) ----------
        const string ClSrc =
            "__kernel void burn(__global float* o, const int iters)\n" +
            "{\n" +
            "  int g = get_global_id(0);\n" +
            "  float a = 1.0f + g * 1e-7f, b = 0.9999f, c = 1.0001f, d = 0.5f, e = 0.25f, f = 0.125f, h = 0.75f, k = 1.5f;\n" +
            "  for (int i = 0; i < iters; i++)\n" +
            "  {\n" +
            "    a = mad(a, b, c); d = mad(d, b, c); e = mad(e, b, c); f = mad(f, b, c);\n" +
            "    h = mad(h, b, c); k = mad(k, b, c); b = mad(b, 0.99999f, 1e-5f); c = mad(c, 0.99999f, 1e-5f);\n" +
            "  }\n" +
            "  o[g] = a + d + e + f + h + k + b + c;\n" +
            "}\n" +
            "__kernel void membw(__global const float* a, __global float* b, const int n)\n" +
            "{\n" +
            "  int g = get_global_id(0);\n" +
            "  int stride = get_global_size(0);\n" +
            "  for (int i = g; i < n; i += stride) b[i] = a[i] * 2.0f + 1.0f;\n" +
            "}\n";

        static void GpuRun(string gpuName)
        {
            uint np;
            if (Cl.clGetPlatformIDs(0, null, out np) != 0 || np == 0) { Fail = "OpenCL nao encontrado (instale o driver da placa de video)."; return; }
            IntPtr[] plats = new IntPtr[np];
            Cl.clGetPlatformIDs(np, plats, out np);
            IntPtr dev = IntPtr.Zero, first = IntPtr.Zero; string devName = null, firstName = null;
            foreach (IntPtr pl in plats)
            {
                uint nd;
                if (Cl.clGetDeviceIDs(pl, 4, 0, null, out nd) != 0 || nd == 0) continue;
                IntPtr[] ds = new IntPtr[nd];
                Cl.clGetDeviceIDs(pl, 4, nd, ds, out nd);
                foreach (IntPtr d in ds)
                {
                    StringBuilder sb = new StringBuilder(256); IntPtr rs;
                    Cl.GetDevStr(d, 0x102B, (IntPtr)256, sb, out rs);
                    string n = sb.ToString().Trim();
                    if (first == IntPtr.Zero) { first = d; firstName = n; }
                    if (dev == IntPtr.Zero && gpuName != null && n.Length > 0 &&
                        (gpuName.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf(gpuName, StringComparison.OrdinalIgnoreCase) >= 0 || GpuNameMatch(n, gpuName))) { dev = d; devName = n; }
                }
            }
            if (dev == IntPtr.Zero) { dev = first; devName = firstName; }
            if (dev == IntPtr.Zero) { Fail = "Nenhuma GPU encontrada pelo OpenCL."; return; }

            ulong gmem = 0, maxAlloc = 0; IntPtr rr;
            Cl.GetDevU64(dev, 0x101F, (IntPtr)8, out gmem, out rr);
            Cl.GetDevU64(dev, 0x1010, (IntPtr)8, out maxAlloc, out rr);

            int err;
            IntPtr ctx = Cl.clCreateContext(IntPtr.Zero, 1, new IntPtr[] { dev }, IntPtr.Zero, IntPtr.Zero, out err);
            if (err != 0 || ctx == IntPtr.Zero) { Fail = "OpenCL: falha ao criar contexto (" + err + ")"; return; }
            IntPtr q = IntPtr.Zero, prog = IntPtr.Zero, kBurn = IntPtr.Zero, kMem = IntPtr.Zero, bufO = IntPtr.Zero, bufA = IntPtr.Zero, bufB = IntPtr.Zero;
            try
            {
                q = Cl.clCreateCommandQueue(ctx, dev, 0, out err);
                if (err != 0) { Fail = "OpenCL: falha na fila de comandos (" + err + ")"; return; }
                prog = Cl.clCreateProgramWithSource(ctx, 1, new string[] { ClSrc }, null, out err);
                if (err != 0) { Fail = "OpenCL: falha ao carregar o programa (" + err + ")"; return; }
                err = Cl.clBuildProgram(prog, 1, new IntPtr[] { dev }, "", IntPtr.Zero, IntPtr.Zero);
                if (err != 0)
                {
                    StringBuilder log = new StringBuilder(4096); IntPtr ls;
                    Cl.clGetProgramBuildInfo(prog, dev, 0x1183, (IntPtr)4096, log, out ls);
                    Fail = "OpenCL: erro ao compilar (" + err + ") " + log.ToString().Trim();
                    return;
                }
                kBurn = Cl.clCreateKernel(prog, "burn", out err);
                kMem = Cl.clCreateKernel(prog, "membw", out err);
                const int items = 1 << 20;
                bufO = Cl.clCreateBuffer(ctx, 1, (IntPtr)(items * 4L), IntPtr.Zero, out err);
                long nel = 32L * 1024 * 1024;
                long cap = (long)Math.Min(maxAlloc, gmem / 6) / 4;
                if (cap > 1024 * 1024 && cap < nel) nel = cap;
                bufA = Cl.clCreateBuffer(ctx, 1, (IntPtr)(nel * 4), IntPtr.Zero, out err);
                bufB = Cl.clCreateBuffer(ctx, 1, (IntPtr)(nel * 4), IntPtr.Zero, out err);
                if (bufO == IntPtr.Zero || bufA == IntPtr.Zero || bufB == IntPtr.Zero) { Fail = "OpenCL: sem memoria de video para o teste."; return; }

                Cl.ArgMem(kBurn, 0, (IntPtr)IntPtr.Size, ref bufO);
                Cl.ArgMem(kMem, 0, (IntPtr)IntPtr.Size, ref bufA);
                Cl.ArgMem(kMem, 1, (IntPtr)IntPtr.Size, ref bufB);
                int nint = (int)nel;
                Cl.ArgInt(kMem, 2, (IntPtr)4, ref nint);

                IntPtr[] gsz = new IntPtr[] { (IntPtr)items };
                int iters = 128, round = 0;
                Stopwatch sw = new Stopwatch();
                Extra = devName;
                while (!Stop)
                {
                    Cl.ArgInt(kBurn, 1, (IntPtr)4, ref iters);
                    sw.Restart();
                    err = Cl.clEnqueueNDRangeKernel(q, kBurn, 1, null, gsz, null, 0, IntPtr.Zero, IntPtr.Zero);
                    if (err == 0) err = Cl.clFinish(q);
                    sw.Stop();
                    if (err != 0) { Fail = "OpenCL: erro " + err + " durante o calculo (o driver pode ter reiniciado)."; break; }
                    double ms = sw.Elapsed.TotalMilliseconds;
                    Th.Interlocked.Add(ref Counter, (long)items * iters * 16);
                    // mantem cada disparo curto (~40-150 ms) para nao acionar o TDR do Windows
                    if (ms < 40 && iters < 65536) iters *= 2; else if (ms > 150 && iters > 16) iters /= 2;
                    if ((++round & 3) == 0)
                    {
                        sw.Restart();
                        for (int i = 0; i < 6 && err == 0; i++)
                        {
                            err = Cl.clEnqueueNDRangeKernel(q, kMem, 1, null, gsz, null, 0, IntPtr.Zero, IntPtr.Zero);
                        }
                        if (err == 0) err = Cl.clFinish(q);
                        sw.Stop();
                        if (err == 0)
                        {
                            double gbs = 6.0 * nel * 8 / 1e9 / Math.Max(0.0001, sw.Elapsed.TotalSeconds);
                            Extra = devName + "  |  VRAM " + gbs.ToString("0") + " GB/s";
                        }
                    }
                }
            }
            finally
            {
                if (bufO != IntPtr.Zero) Cl.clReleaseMemObject(bufO);
                if (bufA != IntPtr.Zero) Cl.clReleaseMemObject(bufA);
                if (bufB != IntPtr.Zero) Cl.clReleaseMemObject(bufB);
                if (kBurn != IntPtr.Zero) Cl.clReleaseKernel(kBurn);
                if (kMem != IntPtr.Zero) Cl.clReleaseKernel(kMem);
                if (prog != IntPtr.Zero) Cl.clReleaseProgram(prog);
                if (q != IntPtr.Zero) Cl.clReleaseCommandQueue(q);
                Cl.clReleaseContext(ctx);
            }
        }
    }

    // ------------------------------------------------------------------
    //  Ventoinhas de todo o PC (GPU + CPU_FAN + bombas + gabinete + coolers)
    // ------------------------------------------------------------------
    class FanCh
    {
        public string Name, Origin, Key, Custom = "";
        public Gpu Gpu; public Sn Rpm; public LHM.ISensor Ctl;
        public int Mode, Manual = 50, CMinT = 40, CMinP = 30, CMaxT = 80, CMaxP = 100, Last = -1, RpmMax;
        public bool Boost;
        public bool Controllable { get { return (Gpu != null && Gpu.CanFan) || Ctl != null; } }
        public string Display { get { return Custom.Length > 0 ? Custom : Name; } }

        // Tipo da ventoinha deduzido pelo nome (voce pode renomear: inclua CPU, Bomba ou Gabinete)
        public string RoleName
        {
            get
            {
                if (Gpu != null) return "GPU";
                string n = (Custom + " " + Name).ToLower();
                if (n.Contains("pump") || n.Contains("aio") || n.Contains("water") || n.Contains("liquid") || n.Contains("bomba")) return "Bomba/AIO";
                if (n.Contains("cpu") || n.Contains("proc")) return "CPU";
                if (n.Contains("chassis") || n.Contains("system") || n.Contains("sys") || n.Contains("case") || n.Contains("front") || n.Contains("rear") ||
                    n.Contains("top") || n.Contains("cha") || n.Contains("gabinete") || n.Contains("frontal") || n.Contains("traseira")) return "Gabinete";
                return "Outra";
            }
        }
        // minimo de seguranca: bomba de AIO nunca abaixo de 60%
        public int MinPct { get { string r = RoleName; return r == "GPU" ? 0 : (r == "Bomba/AIO" ? 60 : 20); } }

        public int? Pct()
        {
            if (Gpu != null) return Gpu.FanPct();
            if (Ctl != null) { float? v = Ctl.Value; return v.HasValue ? (int?)Math.Round(v.Value) : null; }
            return null;
        }
        public int? RpmNow()
        {
            if (Gpu != null) return Gpu.FanRpm();
            return (Rpm != null && Rpm.Last.HasValue) ? (int?)Math.Round(Rpm.Last.Value) : null;
        }
        public bool Set(int pct, out string msg)
        {
            pct = Math.Max(MinPct, Math.Min(100, pct));
            if (Gpu != null) return Gpu.SetFan(pct, out msg);
            if (Ctl != null && Ctl.Control != null)
            {
                try { Ctl.Control.SetSoftware(pct); msg = "OK: " + pct + "%"; return true; }
                catch (Exception ex) { msg = ex.Message; return false; }
            }
            msg = "Esta ventoinha e somente leitura."; return false;
        }
        public bool Auto(out string msg)
        {
            if (Gpu != null) return Gpu.SetFanAuto(out msg);
            if (Ctl != null && Ctl.Control != null)
            {
                try { Ctl.Control.SetDefault(); msg = "OK: automatico"; return true; }
                catch (Exception ex) { msg = ex.Message; return false; }
            }
            msg = "Esta ventoinha e somente leitura."; return false;
        }
    }

    static class FanHub
    {
        public static List<FanCh> All = new List<FanCh>();
        static bool built;
        static string Digits(string t)
        {
            int e = t.Length - 1;
            while (e >= 0 && !char.IsDigit(t[e])) e--;
            if (e < 0) return "";
            int b = e;
            while (b > 0 && char.IsDigit(t[b - 1])) b--;
            return t.Substring(b, e - b + 1);
        }
        public static void Build()
        {
            if (built) return;
            built = true;
            foreach (Gpu g in SensorHub.Gpus)
            {
                FanCh c = new FanCh(); c.Gpu = g; c.Name = "Ventoinhas da GPU"; c.Origin = g.Name; c.Key = "gpu:" + g.Name;
                All.Add(c);
            }
            List<Sn> used = new List<Sn>();
            foreach (Sn s in SensorHub.PcSensors)
            {
                if (s.Type != "Control" || s.Ls == null || s.Ls.Control == null) continue;
                FanCh c = new FanCh(); c.Ctl = s.Ls; c.Name = s.Name; c.Origin = s.Group; c.Key = s.Key;
                string d = Digits(s.Name);
                foreach (Sn f in SensorHub.PcSensors)
                    if (f.Type == "Fan" && f.Group == s.Group && !used.Contains(f) && (f.Name == s.Name || (d.Length > 0 && Digits(f.Name) == d)))
                    { c.Rpm = f; used.Add(f); break; }
                All.Add(c);
            }
            foreach (Sn f in SensorHub.PcSensors)
            {
                if (f.Type != "Fan" || used.Contains(f)) continue;
                FanCh c = new FanCh(); c.Rpm = f; c.Name = f.Name; c.Origin = f.Group; c.Key = f.Key;
                All.Add(c);
            }
            foreach (FanCh c in All) c.Custom = Settings.Get("fan.name." + c.Key, "");
        }
    }

    class FanPage : Panel
    {
        Gpu cur;
        ListView lv = new ListView();
        XpOption optAuto, optManual, chkSafe, chkRestore;
        XpSlider sl; XpText txPct, txRpm, txSafe;
        Label lblMsg;
        Dictionary<FanCh, ListViewItem> items = new Dictionary<FanCh, ListViewItem>();
        List<FanCh> identList = new List<FanCh>();
        DateTime identUntil = DateTime.MinValue;
        LTip tip = new LTip();

        public FanPage()
        {
            Dock = DockStyle.Fill;
            FanHub.Build();
            tip.InitialDelay = 300; tip.AutoPopDelay = 9000;
            BuildList(); BuildMode(); BuildApply(); BuildMenu();
            optAuto.Checked = true;
        }

        public void SetGpu(Gpu g) { cur = g; }

        void BuildList()
        {
            XpGroup g = new XpGroup(); g.Title = "1) Ventoinhas detectadas (Ctrl/Shift = varias)"; g.SetBounds(0, 0, 676, 214);
            lv.SetBounds(10, 22, 656, 136); lv.View = View.Details; lv.FullRowSelect = true; lv.GridLines = true;
            lv.MultiSelect = true; lv.HideSelection = false; lv.BackColor = Xp.EditBg; lv.Font = Xp.Ui;
            lv.Columns.Add("Ventoinha", 160); lv.Columns.Add("Tipo", 75); lv.Columns.Add("Origem", 170); lv.Columns.Add("RPM", 55);
            lv.Columns.Add("%", 50); lv.Columns.Add("Modo", 110);
            UI.DoubleBuffer(lv);
            g.Controls.Add(lv);
            foreach (FanCh c in FanHub.All)
            {
                ListViewItem it = new ListViewItem(new string[] { c.Display, c.Controllable ? c.RoleName : c.RoleName + " (so leitura)", c.Origin, "-", "-", "-" });
                it.Tag = c;
                if (!c.Controllable) it.ForeColor = Xp.Dim;
                lv.Items.Add(it); items[c] = it;
            }
            if (FanHub.All.Count == 0)
                lv.Items.Add(new ListViewItem(new string[] { "Nenhuma ventoinha encontrada", "", "A placa-mae pode nao expor as ventoinhas ao Windows", "", "", "" }));
            XpButton bPawn = UI.Btn(g, PawnIo.Installed() ? "PawnIO instalado" : "Instalar PawnIO", 10, 164, 140, delegate { PawnIo.InstallNow(this); });
            bPawn.Enabled = !PawnIo.Installed();
            tip.SetToolTip(bPawn, "PawnIO: driver usado pelo LibreHardwareMonitor para ler sensores da CPU/placa-mae e controlar as ventoinhas da placa-mae. Clique para baixar e instalar.");
            UI.Btn(g, "Identificar", 416, 164, 80, delegate { Identify(); });
            UI.Btn(g, "Tudo automatico", 502, 164, 120, delegate { AllAuto(); });
            Label h = UI.Lbl(g, "Identificar = liga as selecionadas a 100% por 5 s para voce descobrir qual e qual.  Botao direito na lista: selecionar por tipo (CPU, bombas, gabinete) ou renomear (inclua CPU, Bomba ou Gabinete no nome).", 10, 190, Xp.Ui, Xp.Dim);
            h.MaximumSize = new Size(650, 0);
            tip.SetToolTip(lv, "RPM = giros por minuto. % = potencia enviada. \"so leitura\" = a placa-mae nao permite controle por software.");
            Controls.Add(g);
        }

        void BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip(); m.RenderMode = ToolStripRenderMode.System;
            m.Items.Add("Selecionar todas", null, delegate { foreach (ListViewItem x in lv.Items) x.Selected = true; });
            m.Items.Add("Selecionar so as controlaveis", null, delegate
            {
                foreach (ListViewItem x in lv.Items) { FanCh f = x.Tag as FanCh; x.Selected = f != null && f.Controllable; }
            });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Selecionar: CPU", null, delegate { SelectRole("CPU"); });
            m.Items.Add("Selecionar: bombas / AIO", null, delegate { SelectRole("Bomba/AIO"); });
            m.Items.Add("Selecionar: gabinete", null, delegate { SelectRole("Gabinete"); });
            m.Items.Add("Selecionar: GPU", null, delegate { SelectRole("GPU"); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Renomear...", null, delegate { Rename(); });
            m.Items.Add("Identificar (100% por 5 s)", null, delegate { Identify(); });
            Lang.Menu(m); lv.ContextMenuStrip = m;
        }

        void BuildMode()
        {
            XpGroup g = new XpGroup(); g.Title = "2) Como as selecionadas devem trabalhar?"; g.SetBounds(0, 218, 676, 166);
            optAuto = UI.Opt(g, "Automatico - a placa/BIOS decide (recomendado no dia a dia)", 14, 22, 520, true);
            optManual = UI.Opt(g, "Manual - velocidade fixa em %:", 14, 44, 300, true);
            sl = new XpSlider(); sl.SetBounds(34, 60, 280, 32); sl.Minimum = 0; sl.Maximum = 100; sl.Value = 50; g.Controls.Add(sl);
            txPct = UI.Txt(g, 322, 66, 44, "50"); UI.Lbl(g, "%", 370, 70);
            UI.Bind(sl, txPct);
            UI.Lbl(g, "RPM alvo:", 34, 110);
            txRpm = UI.Txt(g, 96, 106, 60, "1200");
            UI.Btn(g, "Definir por RPM", 166, 104, 112, delegate { ByRpm(); });
            UI.Lbl(g, "(use \"Identificar\" antes: ele mede o RPM maximo de cada ventoinha)", 288, 110, Xp.Ui, Xp.Dim);
            tip.SetToolTip(optAuto, "A propria placa-mae ou o driver controla a velocidade pela temperatura.");
            tip.SetToolTip(optManual, "A ventoinha fica sempre na mesma velocidade, nao importa a temperatura.");
            Controls.Add(g);
        }

        void BuildApply()
        {
            XpGroup g = new XpGroup(); g.Title = "3) Aplicar"; g.SetBounds(0, 388, 676, 80);
            UI.Btn(g, "Aplicar nas selecionadas", 10, 20, 170, delegate { ApplySel(); }).Height = 26;
            chkSafe = UI.Opt(g, "Seguranca: forcar 100% se a temperatura passar de", 10, 54, 320, false); chkSafe.Checked = true;
            txSafe = UI.Txt(g, 336, 51, 40, "85"); UI.Lbl(g, "\u00B0C", 380, 55);
            chkRestore = UI.Opt(g, "Voltar tudo ao automatico ao fechar", 420, 54, 250, false); chkRestore.Checked = true;
            tip.SetToolTip(chkSafe, "Se esquentar demais, as ventoinhas vao a 100% mesmo no modo manual.");
            Controls.Add(g);
            lblMsg = UI.Lbl(this, "", 0, 474, Xp.UiBold, Xp.Good);
            lblMsg.AutoSize = false; lblMsg.SetBounds(0, 474, 676, 40);
        }

        void Msg(string t, bool err) { lblMsg.ForeColor = err ? Xp.Bad : Xp.Good; lblMsg.Text = t; }

        List<FanCh> Selected()
        {
            List<FanCh> l = new List<FanCh>();
            foreach (ListViewItem it in lv.SelectedItems) { FanCh c = it.Tag as FanCh; if (c != null && c.Controllable) l.Add(c); }
            return l;
        }

        void SelectRole(string role)
        {
            lv.Focus();
            int n = 0;
            foreach (ListViewItem it in lv.Items)
            {
                FanCh c = it.Tag as FanCh;
                bool on = c != null && c.Controllable && c.RoleName == role;
                it.Selected = on; if (on) n++;
            }
            Msg(n > 0 ? n + " ventoinha(s) do tipo \"" + role + "\" selecionada(s)." : "Nenhuma ventoinha controlavel do tipo \"" + role + "\" (renomeie as suas para classificar).", n == 0);
        }

        void Rename()
        {
            if (lv.SelectedItems.Count == 0) { Msg("Selecione uma ventoinha para renomear.", true); return; }
            ListViewItem it = lv.SelectedItems[0]; FanCh c = it.Tag as FanCh;
            if (c == null) return;
            string r = PromptForm.Ask(FindForm(), "Renomear ventoinha", "Nome (inclua CPU, Bomba ou Gabinete para definir o tipo):", c.Display);
            if (r == null) return;
            c.Custom = (r == c.Name) ? "" : r;
            Settings.Set("fan.name." + c.Key, c.Custom);
            it.Text = c.Display; it.SubItems[1].Text = Lang.T(c.Controllable ? c.RoleName : c.RoleName + " (so leitura)");
        }

        void ApplySel()
        {
            List<FanCh> sel = Selected();
            if (sel.Count == 0) { Msg("Selecione na lista pelo menos uma ventoinha controlavel.", true); return; }
            int ok = 0; string last = "";
            foreach (FanCh c in sel)
            {
                c.Boost = false; c.Last = -1;
                string m;
                if (optAuto.Checked) { c.Mode = 0; if (c.Auto(out m)) ok++; last = m; }
                else
                {
                    c.Mode = 1; c.Manual = sl.Value;
                    if (c.Set(c.Manual, out m)) { ok++; c.Last = Math.Max(c.MinPct, c.Manual); }
                    last = m;
                }
            }
            Msg(ok + Lang.Pick(" de ", " of ") + sel.Count + " ventoinha(s) aplicada(s). " + last, ok == 0);
        }

        void ByRpm()
        {
            int target = UI.Int(txRpm, 0), ok = 0, nomax = 0;
            foreach (FanCh c in Selected())
            {
                if (c.RpmMax <= 0) { nomax++; continue; }
                int pct = (int)Math.Round(target * 100.0 / c.RpmMax);
                c.Mode = 1; c.Manual = Math.Max(c.MinPct, Math.Min(100, pct)); c.Last = -1; c.Boost = false;
                string m; c.Set(c.Manual, out m); ok++;
            }
            if (ok > 0) optManual.Checked = true;
            Msg(ok + " ajustada(s)." + (nomax > 0 ? " " + nomax + " sem RPM maximo medido: use \"Identificar\" antes." : ""), ok == 0);
        }

        void Identify()
        {
            List<FanCh> sel = Selected();
            if (sel.Count == 0) { Msg("Selecione pelo menos uma ventoinha controlavel.", true); return; }
            foreach (FanCh c in sel) { string m; c.Set(100, out m); if (!identList.Contains(c)) identList.Add(c); }
            identUntil = DateTime.UtcNow.AddSeconds(5);
            Msg("Selecionadas a 100% por 5 s. Veja/ouca qual acelerou e acompanhe o RPM na lista.", false);
        }

        void AllAuto()
        {
            foreach (FanCh c in FanHub.All) if (c.Controllable) { c.Mode = 0; c.Boost = false; string m; c.Auto(out m); }
            optAuto.Checked = true;
            Msg("Todas as ventoinhas voltaram ao automatico.", false);
        }

        public void RestoreAll()
        {
            if (chkRestore == null || !chkRestore.Checked) return;
            foreach (FanCh c in FanHub.All) if (c.Controllable) { string m; try { c.Auto(out m); } catch (Exception) { } }
        }

        float? TempOf(FanCh c)
        {
            if (c.Gpu != null) { int? t = c.Gpu.Temp(); return t.HasValue ? (float?)t.Value : null; }
            return SensorHub.CpuTemp();
        }

        public void Tick(bool visible)
        {
            DateTime now = DateTime.UtcNow;
            if (identList.Count > 0 && now >= identUntil)
            {
                foreach (FanCh c in identList)
                {
                    int? r = c.RpmNow();
                    if (r.HasValue && r.Value > 0) c.RpmMax = r.Value;
                    string m;
                    if (c.Mode == 1) c.Set(c.Manual, out m); else c.Auto(out m);
                }
                identList.Clear();
                Msg("Identificacao concluida: RPM maximo guardado (usado em \"Definir por RPM\").", false);
            }
            bool safeOn = chkSafe.Checked; int safeT = UI.Int(txSafe, 85);
            foreach (FanCh c in FanHub.All)
            {
                if (!c.Controllable || c.Mode != 1 || identList.Contains(c)) continue;
                float? t = TempOf(c);
                if (safeOn && t.HasValue && t.Value >= safeT)
                {
                    if (!c.Boost) { c.Boost = true; string m1; c.Set(100, out m1); Msg("SEGURANCA: " + c.Display + Lang.Pick(" a 100% (", " at 100% (") + t.Value.ToString("0") + " \u00B0C).", true); }
                    continue;
                }
                if (c.Boost) { if (t.HasValue && t.Value < safeT - 5) { c.Boost = false; c.Last = -1; } else continue; }
                if (c.Last < 0 || Math.Abs(c.Manual - c.Last) >= 1)
                {
                    string m2; c.Set(c.Manual, out m2); c.Last = c.Manual;
                }
            }
            if (visible) UpdateUi();
        }

        static void SetSub(ListViewItem it, int i, string t) { t = Lang.T(t); if (it.SubItems[i].Text != t) it.SubItems[i].Text = t; }

        void UpdateUi()
        {
            foreach (KeyValuePair<FanCh, ListViewItem> kv in items)
            {
                FanCh c = kv.Key; ListViewItem it = kv.Value;
                int? r = c.RpmNow(), p = c.Pct();
                SetSub(it, 3, r.HasValue ? r.Value.ToString() : "-");
                SetSub(it, 4, p.HasValue ? p.Value + "%" : "-");
                SetSub(it, 5, !c.Controllable ? "-" : (c.Boost ? "SEGURANCA" : (c.Mode == 0 ? "Automatico" : "Manual " + c.Manual + "%")));
            }
        }
    }

    // ------------------------------------------------------------------
    //  Identificacao da CPU, banco de voltagens e utilitarios
    // ------------------------------------------------------------------
    static class CpuId
    {
        public static string Name = "", Vendor = "", Socket = "";
        public static int Cores, Threads;
        static CpuId()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (k != null)
                    {
                        Name = Convert.ToString(k.GetValue("ProcessorNameString")).Trim();
                        string v = Convert.ToString(k.GetValue("VendorIdentifier"));
                        if (v.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0) Vendor = "AMD";
                        else if (v.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0) Vendor = "Intel";
                        else Vendor = v;
                    }
                }
            }
            catch (Exception) { }
            Threads = Environment.ProcessorCount;
        }
        public static bool Amd { get { return Vendor == "AMD"; } }

        // AM4 / AM5 / soquete Intel / mobile
        public static string Platform()
        {
            string s = (Socket ?? "").ToUpper();
            if (Amd)
            {
                if (s.Contains("AM5")) return "AM5";
                if (s.Contains("AM4")) return "AM4";
                int i = Name.IndexOf("Ryzen", StringComparison.OrdinalIgnoreCase);
                if (i >= 0)
                {
                    int j = i + 5;
                    while (j < Name.Length && !char.IsDigit(Name[j])) j++;
                    // pula o "3/5/7/9" do nivel e procura o numero do modelo (4 digitos)
                    j++;
                    while (j < Name.Length && !char.IsDigit(Name[j])) j++;
                    if (j < Name.Length)
                    {
                        char d = Name[j];
                        bool mobile = Name.IndexOf(" U", StringComparison.Ordinal) >= 0 || Name.IndexOf("HS", StringComparison.Ordinal) >= 0 ||
                                      Name.IndexOf("HX", StringComparison.Ordinal) >= 0 || Name.EndsWith("H") || Name.IndexOf("Mobile", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (mobile) return "Mobile (soldada)";
                        if (d >= '1' && d <= '5') return "AM4";
                        if (d >= '7' && d <= '9') return "AM5";
                    }
                }
                return s.Length > 0 ? s : "AMD (plataforma nao identificada)";
            }
            return s.Length > 0 ? s : (Vendor == "Intel" ? "Intel (soquete nao identificado)" : "Desconhecida");
        }
        public static int CoMin { get { return Platform() == "AM5" ? -50 : -30; } }
        public static int CoMax { get { return 30; } }
    }

    static class Proc
    {
        // roda um programa e devolve a saida (stdout + stderr) e o codigo de saida
        public static string Run(string exe, string args, int timeoutMs, out int code)
        {
            code = -1;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch (Exception) { } return o + "\n(tempo esgotado)"; }
                    code = p.ExitCode;
                    return o.Trim();
                }
            }
            catch (Exception ex) { return "Erro ao executar " + exe + ": " + ex.Message; }
        }
    }

    // ------------------------------------------------------------------
    //  Memoria RAM: modulos (WMI), canais, voltagem e planilha de timings
    // ------------------------------------------------------------------
    class RamMod
    {
        public string Slot = "", Bank = "", Maker = "", Part = "", Type = "?";
        public long Cap; public int Speed, Conf, Volt, Rank; public bool Ecc;
    }

    static class RamInfo
    {
        static int I(string s) { int v; return int.TryParse(s, out v) ? v : 0; }
        static string TypeName(int smbios)
        {
            switch (smbios)
            {
                case 20: return "DDR"; case 21: return "DDR2"; case 24: return "DDR3"; case 26: return "DDR4"; case 34: return "DDR5";
                case 27: return "LPDDR"; case 28: return "LPDDR2"; case 29: return "LPDDR3"; case 30: return "LPDDR4"; case 35: return "LPDDR5";
                default: return "?";
            }
        }
        public static List<RamMod> Collect()
        {
            List<RamMod> l = new List<RamMod>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory"))
                using (ManagementObjectCollection col = s.Get())
                    foreach (ManagementBaseObject mo in col)
                    {
                        RamMod m = new RamMod();
                        m.Slot = CpuInfo.P(mo, "DeviceLocator") ?? ""; m.Bank = CpuInfo.P(mo, "BankLabel") ?? "";
                        m.Maker = (CpuInfo.P(mo, "Manufacturer") ?? "").Trim(); m.Part = (CpuInfo.P(mo, "PartNumber") ?? "").Trim();
                        long cap; long.TryParse(CpuInfo.P(mo, "Capacity") ?? "0", out cap); m.Cap = cap;
                        m.Speed = I(CpuInfo.P(mo, "Speed")); m.Conf = I(CpuInfo.P(mo, "ConfiguredClockSpeed")); m.Volt = I(CpuInfo.P(mo, "ConfiguredVoltage"));
                        m.Rank = I(CpuInfo.P(mo, "Attributes")) & 0xF;
                        int sm = I(CpuInfo.P(mo, "SMBIOSMemoryType")); if (sm == 0) sm = I(CpuInfo.P(mo, "MemoryType"));
                        m.Type = TypeName(sm);
                        m.Ecc = I(CpuInfo.P(mo, "TotalWidth")) > I(CpuInfo.P(mo, "DataWidth")) && I(CpuInfo.P(mo, "DataWidth")) > 0;
                        l.Add(m); mo.Dispose();
                    }
            }
            catch (Exception) { }
            return l;
        }
        public static int Channels(List<RamMod> mods, out bool estimated)
        {
            estimated = false;
            HashSet<string> ch = new HashSet<string>();
            foreach (RamMod m in mods)
            {
                string c = null, u = (m.Slot + " " + m.Bank).ToUpper();
                int i = u.IndexOf("CHANNEL");
                if (i >= 0)
                {
                    int j = i + 7;
                    while (j < u.Length && u[j] == ' ') j++;
                    if (j < u.Length && char.IsLetterOrDigit(u[j])) c = u[j].ToString();
                }
                if (c == null)
                    for (int k = 0; k + 1 < m.Slot.Length; k++)
                    {
                        char a = char.ToUpper(m.Slot[k]), b = m.Slot[k + 1];
                        if (a >= 'A' && a <= 'H' && char.IsDigit(b) && (k == 0 || !char.IsLetter(m.Slot[k - 1]))) { c = a.ToString(); break; }
                    }
                if (c == null) { estimated = true; break; }
                ch.Add(c);
            }
            if (estimated) return mods.Count >= 2 ? 2 : Math.Max(1, mods.Count);
            return Math.Max(1, ch.Count);
        }
        public static string ChannelText(int n)
        {
            switch (n)
            {
                case 1: return "Single channel"; case 2: return "Dual channel"; case 3: return "Triple channel"; case 4: return "Quad channel";
                case 6: return "Hexa channel"; case 8: return "Octa channel"; default: return n + " canais";
            }
        }
    }

    // ------------------------------------------------------------------
    //  Timings basicos (como CPU-Z / HWiNFO): le direto do controlador de memoria da CPU
    //  usando o driver de baixo nivel (Ring0) que o LibreHardwareMonitor ja carrega.
    //  Valores: tCAS, tRCD, tRP, tRAS, tRC, tRFC e Command Rate.
    // ------------------------------------------------------------------
    static class MemRead
    {
        public class Result
        {
            public Dictionary<string, string> V = new Dictionary<string, string>();
            public string Source = "", Note = "";
        }

        // ---------- AMD Zen: registradores UMC via SMN (porta indice/dado 0x60/0x64 da ponte host) ----------
        static MethodInfo mWr, mRd; static bool inited, okRing;
        static void Init()
        {
            if (inited) return;
            inited = true;
            try
            {
                Type t = typeof(LHM.Computer).Assembly.GetType("LibreHardwareMonitor.Hardware.Ring0");
                if (t == null) return;
                BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                mWr = t.GetMethod("WritePciConfig", f); mRd = t.GetMethod("ReadPciConfig", f);
                okRing = mWr != null && mRd != null;
            }
            catch (Exception) { }
        }
        // LibreHardwareMonitor novo: modulo PawnIO "AmdFamily17" (metodo ReadSmn), achado por reflexao
        static object pawnObj; static MethodInfo pawnRd; static bool pawnTried;
        static bool PawnSmn(uint addr, out uint val)
        {
            val = 0;
            try
            {
                if (!pawnTried)
                {
                    pawnTried = true;
                    foreach (Type t in typeof(LHM.Computer).Assembly.GetTypes())
                    {
                        if (t.Name.IndexOf("AmdFamily17", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                        MethodInfo m = null;
                        foreach (MethodInfo mi in t.GetMethods(f))
                        {
                            ParameterInfo[] ps = mi.GetParameters();
                            if (mi.Name == "ReadSmn" && ps.Length >= 1 && ps[0].ParameterType == typeof(uint)) { m = mi; break; }
                        }
                        if (m == null) continue;
                        object o = null;
                        if (!m.IsStatic)
                        {
                            ConstructorInfo c = t.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                            if (c == null) continue;
                            o = c.Invoke(null);
                        }
                        pawnObj = o; pawnRd = m; break;
                    }
                }
                if (pawnRd == null) return false;
                ParameterInfo[] pp = pawnRd.GetParameters();
                object[] args = new object[pp.Length];
                args[0] = addr;
                for (int i = 1; i < pp.Length; i++) args[i] = pp[i].ParameterType.IsByRef ? (object)0u : (pp[i].ParameterType.IsValueType ? Activator.CreateInstance(pp[i].ParameterType) : null);
                object res = pawnRd.Invoke(pawnObj, args);
                if (pawnRd.ReturnType == typeof(uint)) { val = (uint)res; return true; }
                if (pawnRd.ReturnType == typeof(bool) && (bool)res && pp.Length > 1 && args[1] is uint) { val = (uint)args[1]; return true; }
                return false;
            }
            catch (Exception) { return false; }
        }

        // Acesso direto ao PawnIOLib.dll com o modulo AmdFamily17 embutido na DLL do LibreHardwareMonitor
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DPOpen(out IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DPLoad(IntPtr h, byte[] blob, UIntPtr size);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DPExec(IntPtr h, [MarshalAs(UnmanagedType.LPStr)] string name, long[] inp, UIntPtr inN, long[] outp, UIntPtr outN, out UIntPtr ret);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryW(string n);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr m, string n);
        static IntPtr dpH; static DPExec dpExec; static bool dpTried; public static string DiagWhy = "";
        static bool DirectSmn(uint addr, out uint val)
        {
            val = 0;
            try
            {
                if (!dpTried)
                {
                    dpTried = true;
                    string dll = null;
                    foreach (string e in new string[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetEnvironmentVariable("ProgramFiles") })
                        if (!string.IsNullOrEmpty(e) && File.Exists(Path.Combine(Path.Combine(e, "PawnIO"), "PawnIOLib.dll"))) { dll = Path.Combine(Path.Combine(e, "PawnIO"), "PawnIOLib.dll"); break; }
                    if (dll == null) { DiagWhy += "PawnIOLib.dll nao encontrada; "; return false; }
                    Assembly asm = typeof(LHM.Computer).Assembly;
                    byte[] blob = null;
                    foreach (string rn in asm.GetManifestResourceNames())
                        if (rn.EndsWith("AmdFamily17.bin", StringComparison.OrdinalIgnoreCase))
                            using (Stream st = asm.GetManifestResourceStream(rn)) { MemoryStream ms = new MemoryStream(); st.CopyTo(ms); blob = ms.ToArray(); }
                    if (blob == null) { DiagWhy += "a LibreHardwareMonitorLib.dll da pasta libs nao traz o modulo PawnIO (DLL antiga: atualize); "; return false; }
                    IntPtr lib = LoadLibraryW(dll);
                    if (lib == IntPtr.Zero) { DiagWhy += "falha ao carregar PawnIOLib.dll; "; return false; }
                    IntPtr po = GetProcAddress(lib, "pawnio_open"), pl = GetProcAddress(lib, "pawnio_load"), px = GetProcAddress(lib, "pawnio_execute");
                    if (po == IntPtr.Zero || pl == IntPtr.Zero || px == IntPtr.Zero) { DiagWhy += "PawnIOLib.dll sem as funcoes esperadas; "; return false; }
                    DPOpen fo = (DPOpen)Marshal.GetDelegateForFunctionPointer(po, typeof(DPOpen));
                    DPLoad fl = (DPLoad)Marshal.GetDelegateForFunctionPointer(pl, typeof(DPLoad));
                    IntPtr h;
                    int hr = fo(out h);
                    if (hr < 0) { DiagWhy += "pawnio_open falhou (0x" + hr.ToString("X8") + "; rode como administrador); "; return false; }
                    hr = fl(h, blob, (UIntPtr)(uint)blob.Length);
                    if (hr < 0) { DiagWhy += "pawnio_load falhou (0x" + hr.ToString("X8") + "); "; return false; }
                    dpH = h; dpExec = (DPExec)Marshal.GetDelegateForFunctionPointer(px, typeof(DPExec));
                }
                if (dpExec == null) return false;
                long[] o = new long[1]; UIntPtr ret;
                int r = dpExec(dpH, "ioctl_read_smn", new long[] { addr }, (UIntPtr)1u, o, (UIntPtr)1u, out ret);
                if (r < 0) return false;
                val = (uint)o[0];
                return true;
            }
            catch (Exception ex) { DiagWhy += ex.Message + "; "; return false; }
        }

        static bool Smn(uint addr, out uint val)
        {
            val = 0;
            Init();
            if (okRing && RingSmn(addr, out val)) return true;
            if (PawnSmn(addr, out val)) return true;
            return DirectSmn(addr, out val);
        }
        static bool RingSmn(uint addr, out uint val)
        {
            val = 0;
            System.Threading.Mutex m = null; bool got = false;
            try
            {
                m = new System.Threading.Mutex(false, "Global\\Access_PCI");
                got = m.WaitOne(500);
                if (!got) return false;
                object r1 = mWr.Invoke(null, new object[] { 0u, 0x60u, addr });
                object[] a = new object[] { 0u, 0x64u, 0u };
                object r2 = mRd.Invoke(null, a);
                if (!(r1 is bool) || !(r2 is bool) || !(bool)r1 || !(bool)r2) return false;
                val = (uint)a[2];
                return true;
            }
            catch (Exception) { return false; }
            finally { if (got && m != null) { try { m.ReleaseMutex(); } catch (Exception) { } } if (m != null) m.Close(); }
        }

        static bool T1(uint r, out int cl, out int ras, out int rcd)
        {
            cl = (int)(r & 0x3F); ras = (int)((r >> 8) & 0x7F); rcd = (int)((r >> 16) & 0x3F); int wr = (int)((r >> 24) & 0x3F);
            return (r & 0xC0) == 0 && cl >= 6 && cl <= 70 && rcd >= 6 && rcd <= 70 && wr >= 6 && wr <= 70 && ras >= 15 && ras <= 140 && ras >= rcd;
        }

        static Result Native()
        {
            Result r = new Result();
            if (!CpuId.Amd) { r.Note = "leitura direta do controlador: só CPUs AMD Ryzen nesta versão"; return r; }
            Init();
            bool anyRead = false;
            for (int ch = 0; ch < 4; ch++)
            {
                uint off = (uint)ch * 0x100000u;
                uint[] R = new uint[20]; bool[] ok = new bool[20]; int cnt = 0;
                for (int i = 0; i < 20; i++) { ok[i] = Smn(off + 0x50200u + (uint)(i * 4), out R[i]); if (ok[i]) cnt++; }
                if (cnt == 0) continue;
                anyRead = true;
                // procura o registrador de tCL/tRAS/tRCD (a posicao varia entre geracoes/AGESA)
                int i1 = -1, cl = 0, ras = 0, rcd = 0;
                for (int i = 0; i < 20 && i1 < 0; i++) if (ok[i] && T1(R[i], out cl, out ras, out rcd)) i1 = i;
                if (i1 < 0) continue;
                r.V["tCAS"] = cl.ToString(); r.V["tRCD"] = rcd.ToString(); r.V["tRAS"] = ras.ToString();
                for (int k = 0; k < 20; k++)
                {
                    int j = (i1 + 1 + k) % 20;
                    if (j == i1 || !ok[j]) continue;
                    int c = (int)(R[j] & 0xFF), p = (int)((R[j] >> 16) & 0x3F);
                    if (p >= 6 && p <= 70 && c >= ras + p - 3 && c <= ras + p + 8) { r.V["tRP"] = p.ToString(); r.V["tRC"] = c.ToString(); break; }
                }
                uint[] rf = { 0x50260u, 0x50264u, 0x50268u, 0x5025Cu, 0x50270u };
                foreach (uint ra in rf)
                {
                    uint v;
                    if (!Smn(off + ra, out v)) continue;
                    int f1 = (int)(v & 0x7FF), f2 = (int)((v >> 11) & 0x7FF), f3 = (int)((v >> 22) & 0x3FF);
                    if (f1 >= 80 && f1 <= 1500 && f2 >= 40 && f2 <= f1 && f3 >= 20 && f3 <= f2) { r.V["tRFC"] = f1.ToString(); break; }
                }
                uint cfg;
                if (Smn(off + 0x50100u, out cfg)) r.V["CR"] = ((cfg >> 10) & 1) != 0 ? "2T" : "1T";
                r.Source = "controlador de memória da CPU (AMD Ryzen, canal " + (char)('A' + ch) + ")";
                return r;
            }
            r.Note = anyRead ? "registradores do controlador nao reconhecidos nesta CPU (use o botao Diagnostico)"
                : "nao foi possivel ler o controlador de memoria: " + (PawnIo.Installed() ? "" : "PawnIO nao instalado; ") + DiagWhy + "rode como Administrador";
            return r;
        }

        // ---------- Plano B: relatorio do CPU-Z, se o CPU-Z estiver na pasta do programa ----------
        static string FindCpuz()
        {
            string b = AppDomain.CurrentDomain.BaseDirectory;
            string saved = Settings.Get("cpuzpath", "");
            if (saved.Length > 0 && File.Exists(saved)) return saved;
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            List<string> dirs = new List<string> { b, Path.Combine(b, "tools"), Path.Combine(b, "cpuz"), Path.Combine(b, "tools\\cpuz"), Path.Combine(b, "libs"),
                Path.Combine(pf, "CPUID\\CPU-Z"), Path.Combine(pf86, "CPUID\\CPU-Z"), desk, dl };
            string[] exes = { "cpuz_x64.exe", "cpuz.exe", "cpuz_x32.exe" };
            foreach (string d in dirs) foreach (string e in exes) { string p = Path.Combine(d, e); if (File.Exists(p)) return p; }
            // pastas "cpu-z*" dentro de Downloads/Desktop (versao portatil extraida)
            foreach (string root in new string[] { desk, dl })
                try
                {
                    foreach (string sub in Directory.GetDirectories(root, "*cpu*z*"))
                        foreach (string e in exes) { string p = Path.Combine(sub, e); if (File.Exists(p)) return p; }
                }
                catch (Exception) { }
            return null;
        }

        static Result FromCpuz()
        {
            Result r = new Result();
            string exe = FindCpuz();
            if (exe == null) return r;
            string tmp = Path.Combine(Path.GetTempPath(), "bunos_cpuz_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            int code;
            Proc.Run(exe, "-txt=" + tmp, 40000, out code);
            string f = tmp + ".txt";
            if (!File.Exists(f)) return r;
            string text = File.ReadAllText(f);
            try { File.Delete(f); } catch (Exception) { }
            string[] lines = text.Replace("\r", "").Split('\n');
            int from = 0, to = lines.Length;
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].IndexOf("Memory Timings", StringComparison.OrdinalIgnoreCase) >= 0) { from = i; to = Math.Min(lines.Length, i + 26); break; }
            string[][] syn = {
                new string[] { "tCAS", @"CAS#?\s*Latency", @"\bTcas\b" }, new string[] { "tRCD", @"RAS#?\s*to\s*CAS#?", @"\bTrcd\b" },
                new string[] { "tRP", @"RAS#?\s*Precharge", @"\bTrp\b" }, new string[] { "tRAS", @"Cycle\s*Time\s*\(tRAS\)", @"\bTras\b" },
                new string[] { "tRC", @"Bank\s*Cycle\s*Time", @"\bTrc\b" }, new string[] { "tRFC", @"Row\s*Refresh\s*Cycle", @"\bTrfc\b" },
                new string[] { "CR", @"Command\s*Rate" } };
            for (int i = from; i < to; i++)
                foreach (string[] sy in syn)
                {
                    if (r.V.ContainsKey(sy[0])) continue;
                    for (int k = 1; k < sy.Length; k++)
                    {
                        Match m = Regex.Match(lines[i], sy[k], RegexOptions.IgnoreCase);
                        if (!m.Success) continue;
                        Match n = Regex.Match(lines[i].Substring(m.Index + m.Length), @"(\d+(?:[.,]\d+)?)\s*(T)?\b");
                        if (!n.Success) continue;
                        string v = n.Groups[1].Value.Replace(',', '.');
                        if (v.EndsWith(".0")) v = v.Substring(0, v.Length - 2);
                        if (sy[0] == "CR") v += "T";
                        r.V[sy[0]] = v; break;
                    }
                }
            r.Source = "relatório do CPU-Z (" + Path.GetFileName(exe) + ")";
            return r;
        }

        // Tenta o controlador de memoria; se nao der, o CPU-Z (se existir)
        public static string Diagnose()
        {
            StringBuilder sb = new StringBuilder();
            Init();
            sb.AppendLine("CPU: " + CpuId.Vendor);
            sb.AppendLine("PawnIO instalado: " + PawnIo.Installed());
            sb.AppendLine("Ring0 (LHM) com PCI: " + okRing);
            try
            {
                Assembly asm = typeof(LHM.Computer).Assembly;
                sb.AppendLine("LibreHardwareMonitorLib: " + asm.GetName().Version);
                int n = 0;
                foreach (Type t in asm.GetTypes())
                    if (t.Name.IndexOf("Pawn", StringComparison.OrdinalIgnoreCase) >= 0 || t.Name.IndexOf("AmdFamily", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (n++ > 14) break;
                        StringBuilder ms = new StringBuilder();
                        foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                            if (mi.Name.IndexOf("Smn", StringComparison.OrdinalIgnoreCase) >= 0) ms.Append(mi.Name + " ");
                        sb.AppendLine("  tipo " + t.FullName + (ms.Length > 0 ? "  [" + ms + "]" : ""));
                    }
                if (n == 0) sb.AppendLine("  nenhum tipo PawnIO na DLL (DLL antiga: atualize a LibreHardwareMonitorLib.dll)");
                n = 0;
                foreach (string rn in asm.GetManifestResourceNames())
                    if (rn.IndexOf("Pawn", StringComparison.OrdinalIgnoreCase) >= 0) { if (n++ > 10) break; sb.AppendLine("  recurso " + rn); }
            }
            catch (Exception ex) { sb.AppendLine("erro ao inspecionar a DLL: " + ex.Message); }
            uint[] addrs = { 0x50200, 0x50204, 0x50208, 0x5020C, 0x50210, 0x50214, 0x50218, 0x5021C, 0x50220, 0x50224, 0x50228, 0x5025C, 0x50260, 0x50264, 0x50268, 0x50100 };
            foreach (uint a in addrs)
            {
                uint v1 = 0, v2 = 0, v3 = 0;
                bool o1 = okRing && RingSmn(a, out v1), o2 = PawnSmn(a, out v2), o3 = DirectSmn(a, out v3);
                sb.AppendLine("SMN 0x" + a.ToString("X") + ": " + (o2 ? "0x" + v2.ToString("X8") : (o3 ? "0x" + v3.ToString("X8") : (o1 ? "0x" + v1.ToString("X8") : "falhou"))));
            }
            sb.AppendLine("Motivos: " + DiagWhy);
            return sb.ToString();
        }

        public static Result Read()
        {
            Result n;
            try { n = Native(); } catch (Exception ex) { n = new Result(); n.Note = ex.Message; }
            if (n.V.Count >= 6) return n;
            Result z = null;
            try { z = FromCpuz(); } catch (Exception) { }
            if (z != null && z.V.Count > n.V.Count) return z;
            if (n.V.Count > 0) return n;
            if (n.Note.Length == 0) n.Note = "esta plataforma (" + CpuId.Vendor + ") não expõe os timings sem um driver próprio" + (FindCpuz() == null ? " e o CPU-Z não foi encontrado" : " e o CPU-Z não retornou dados");
            return n;
        }
    }

    // ------------------------------------------------------------------
    //  Pagina de memoria RAM: resumo + modulos + timings basicos
    // ------------------------------------------------------------------
    class RamPage : Panel
    {
        Label lblType, lblCap, lblSpeed, lblChan, lblVolt, lblRank, lblBw, lblSrc, lblLat;
        Label[] tv = new Label[7];
        static readonly string[] TKeys = { "tCAS", "tRCD", "tRP", "tRAS", "tRC", "tRFC", "CR" };
        static readonly string[] TNames = { "tCAS (CL):", "tRCD:", "tRP:", "tRAS:", "tRC:", "tRFC:", "Command Rate:" };
        ListView lvMod = new ListView();
        int speed, channels = 1;
        bool reading, readOnce;
        Dictionary<string, string> tim = new Dictionary<string, string>();

        public RamPage()
        {
            Dock = DockStyle.Fill;
            BuildSummary(); BuildMods(); BuildTimings(); BuildLatency();
            VisibleChanged += delegate { if (Visible && !readOnce) { readOnce = true; ReadAsync(); } };
        }

        Label Val(Control g, string cap, int x, int y, int w)
        {
            Label c = UI.Lbl(g, cap, x, y);
            Label v = UI.Lbl(g, "-", x + 128, y, Xp.UiBold, Xp.GroupText);
            v.AutoSize = false; v.AutoEllipsis = true; v.SetBounds(x + 128, y, w, 18);
            return v;
        }

        // ---------- latencia de memoria (estilo AIDA64): ponteiro encadeado aleatorio, de 4 KB a 256 MB ----------
        LatGraph latGraph = new LatGraph(); Label lblLatBig, lblLatSub, lblLatSteps; XpProgress pbLat; XpButton bLat; bool latBusy, latStop;
        public static int LatSink;

        void BuildLatency()
        {
            XpGroup g = new XpGroup(); g.Title = "Latencia da memoria (ns)"; g.SetBounds(0, 442, 676, 142);
            lblLatBig = UI.Lbl(g, "- ns", 14, 28, Xp.Big, Xp.GroupText);
            lblLatBig.Font = new Font(Xp.Big.FontFamily, 22f, FontStyle.Bold);
            lblLatSub = UI.Lbl(g, "Latencia da RAM (bloco de 256 MB)", 14, 66, Xp.Ui, Xp.Dim);
            lblLatSub.AutoSize = false; lblLatSub.SetBounds(14, 66, 200, 32);
            bLat = UI.Btn(g, "Medir latencia", 14, 102, 130, delegate { ToggleLat(); });
            bLat.Height = 28;
            pbLat = new XpProgress(); pbLat.SetBounds(150, 108, 64, 14); g.Controls.Add(pbLat);
            latGraph.SetBounds(222, 24, 444, 82); g.Controls.Add(latGraph);
            lblLatSteps = UI.Lbl(g, "", 222, 108, Xp.Ui, Xp.Dim);
            lblLatSteps.AutoSize = false; lblLatSteps.SetBounds(222, 108, 444, 30);
            Controls.Add(g);
        }

        void ToggleLat()
        {
            if (latBusy) { latStop = true; return; }
            latBusy = true; latStop = false;
            bLat.Text = "Parar"; latGraph.Kb.Clear(); latGraph.Ns.Clear(); latGraph.Invalidate();
            lblLatBig.Text = "..."; lblLatSteps.Text = "Medindo (feche outros programas para um resultado melhor)..."; pbLat.Value = 0;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { LatRun(); });
        }

        void LatRun()
        {
            Th.Thread me = Th.Thread.CurrentThread;
            Th.ThreadPriority old = me.Priority;
            string err = null;
            try
            {
                me.Priority = Th.ThreadPriority.Highest;
                long[] kbs = { 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536, 131072, 262144 };
                for (int s = 0; s < kbs.Length && !latStop; s++)
                {
                    double ns = LatMeasure(kbs[s] * 1024L);
                    double kb = kbs[s], v = ns; int pct = (s + 1) * 100 / kbs.Length;
                    try { BeginInvoke(new Action(delegate { latGraph.Kb.Add(kb); latGraph.Ns.Add(v); latGraph.Invalidate(); pbLat.Value = pct; lblLatBig.Text = v.ToString("0.0") + " ns"; })); } catch (Exception) { }
                }
            }
            catch (Exception ex) { err = ex.Message; }
            me.Priority = old;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    latBusy = false; bLat.Text = "Medir latencia"; pbLat.Value = 0;
                    int n = latGraph.Ns.Count;
                    if (n == 0) { lblLatBig.Text = "- ns"; lblLatSteps.Text = err != null ? "Falhou: " + err : "Cancelado."; return; }
                    lblLatBig.Text = latGraph.Ns[n - 1].ToString("0.0") + " ns";
                    lblLatSub.Text = "Latencia da RAM (bloco de " + (latGraph.Kb[n - 1] >= 1024 ? (latGraph.Kb[n - 1] / 1024).ToString("0") + " MB" : latGraph.Kb[n - 1].ToString("0") + " KB") + ")";
                    StringBuilder sb = new StringBuilder();
                    double[] want = { 16, 256, 8192 };
                    string[] nm = { "16 KB", "256 KB", "8 MB" };
                    for (int i = 0; i < want.Length; i++) { int k = latGraph.Kb.IndexOf(want[i]); if (k >= 0) sb.Append(nm[i] + ": " + latGraph.Ns[k].ToString("0.0") + " ns    "); }
                    lblLatSteps.Text = sb.ToString() + (latStop ? "(interrompido)" : "") + (err != null ? " " + err : "");
                }));
            }
            catch (Exception) { }
        }

        // tempo medio (ns) de um acesso dependente a um bloco de 'bytes' (passo de 64 B em ordem aleatoria)
        static double LatMeasure(long bytes)
        {
            int lines = (int)(bytes / 64);
            int[] a = new int[lines * 16];
            int[] perm = new int[lines];
            for (int i = 0; i < lines; i++) perm[i] = i;
            Random rnd = new Random(12345);
            for (int i = lines - 1; i > 0; i--) { int j = rnd.Next(i); int t = perm[i]; perm[i] = perm[j]; perm[j] = t; }   // Sattolo: um unico ciclo
            for (int i = 0; i < lines; i++) a[perm[i] * 16] = perm[(i + 1) % lines] * 16;
            long steps = Math.Max(3000000L, lines * 2L);
            int p = 0;
            for (long i = 0; i < lines; i++) p = a[p];   // aquecimento: traz o bloco para o cache/TLB
            double best = double.MaxValue;
            int reps = bytes <= 8L * 1024 * 1024 ? 3 : 2;
            for (int r = 0; r < reps; r++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                for (long i = 0; i < steps; i++) p = a[p];
                sw.Stop();
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e6 / steps);
            }
            LatSink += p;
            return best;
        }

        void BuildSummary()
        {
            XpGroup g = new XpGroup(); g.Title = "Resumo da memória"; g.SetBounds(0, 0, 676, 120);
            lblType = Val(g, "Tipo:", 14, 24, 190); lblCap = Val(g, "Capacidade total:", 14, 46, 190); lblSpeed = Val(g, "Velocidade:", 14, 68, 190);
            lblChan = Val(g, "Canais:", 350, 24, 180); lblVolt = Val(g, "Voltagem:", 350, 46, 180); lblRank = Val(g, "Ranks / ECC:", 350, 68, 180);
            lblBw = UI.Lbl(g, "", 14, 92, Xp.Ui, Xp.Dim);
            lblBw.AutoSize = false; lblBw.AutoEllipsis = true; lblBw.SetBounds(14, 92, 650, 20);
            Controls.Add(g);
        }

        void BuildMods()
        {
            lvMod.SetBounds(0, 126, 676, 104); lvMod.View = View.Details; lvMod.FullRowSelect = true; lvMod.GridLines = true;
            lvMod.BackColor = Xp.EditBg; lvMod.Font = Xp.Ui;
            lvMod.Columns.Add("Slot", 140); lvMod.Columns.Add("Cap.", 55); lvMod.Columns.Add("Fabricante", 90); lvMod.Columns.Add("Part number", 150);
            lvMod.Columns.Add("MT/s", 50); lvMod.Columns.Add("Volt", 55); lvMod.Columns.Add("Tipo", 50); lvMod.Columns.Add("Ranks", 50);
            lvMod.Items.Add(new ListViewItem(new string[] { "Lendo módulos...", "", "", "", "", "", "", "" }));
            Controls.Add(lvMod);
        }

        void BuildTimings()
        {
            XpGroup g = new XpGroup(); g.Title = "Timings atuais (leitura automática)"; g.SetBounds(0, 238, 676, 196);
            for (int i = 0; i < 7; i++)
            {
                int col = i < 4 ? 0 : 1, row = i < 4 ? i : i - 4;
                UI.Lbl(g, TNames[i], 20 + col * 330, 32 + row * 28, Xp.UiBold);
                Label v = UI.Lbl(g, "-", 160 + col * 330, 28 + row * 28, Xp.UiBold, Xp.GroupText);
                v.AutoSize = false; v.SetBounds(160 + col * 330, 28 + row * 28, 150, 22);
                v.Font = Xp.Big; v.AutoEllipsis = true;
                tv[i] = v;
            }
            lblLat = UI.Lbl(g, "", 350, 116, Xp.Ui, Xp.Dim);
            lblLat.AutoSize = false; lblLat.AutoEllipsis = true; lblLat.SetBounds(350, 116, 316, 20);
            UI.Btn(g, "Indicar CPU-Z...", 456, 138, 100, delegate
            {
                using (OpenFileDialog od = new OpenFileDialog())
                {
                    od.Filter = Lang.T("CPU-Z (cpuz*.exe)|cpuz*.exe|Programas|*.exe"); od.Title = Lang.T("Escolha o cpuz.exe (versao portatil ou instalada)");
                    if (od.ShowDialog(this) != DialogResult.OK) return;
                    Settings.Set("cpuzpath", od.FileName);
                }
                ReadAsync();
            });
            UI.Btn(g, "Baixar CPU-Z", 566, 138, 100, delegate { try { Process.Start("https://www.cpuid.com/softwares/cpu-z.html"); } catch (Exception) { } });
            lblSrc = UI.Lbl(g, "Lendo os timings...", 20, 144, Xp.Ui, Xp.Dim);
            lblSrc.AutoSize = false; lblSrc.SetBounds(20, 144, 320, 44);
            UI.Btn(g, "Ler novamente", 566, 164, 100, delegate { ReadAsync(); });
            UI.Btn(g, "Diagnostico", 456, 164, 100, delegate
            {
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    string d;
                    try { d = MemRead.Diagnose(); } catch (Exception ex) { d = ex.ToString(); }
                    try { BeginInvoke(new Action(delegate { try { Clipboard.SetText(d); } catch (Exception) { } Dlg.Show(this, d + "\n(copiado para a area de transferencia)", "BunoTunner - diagnostico dos timings"); })); } catch (Exception) { }
                });
            });
            Controls.Add(g);
        }

        public void ReadAsync()
        {
            if (reading) return;
            reading = true;
            lblSrc.ForeColor = Xp.Dim; lblSrc.Text = "Lendo os timings direto do controlador de memória...";
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                MemRead.Result r;
                try { r = MemRead.Read(); } catch (Exception ex) { r = new MemRead.Result(); r.Note = ex.Message; }
                try { BeginInvoke(new Action(delegate { ShowRead(r); })); } catch (Exception) { }
            });
        }

        void ShowRead(MemRead.Result r)
        {
            reading = false;
            tim = r.V;
            for (int i = 0; i < 7; i++)
            {
                string v; tim.TryGetValue(TKeys[i], out v);
                tv[i].Text = v == null ? "-" : v;
            }
            if (r.V.Count > 0) { lblSrc.ForeColor = Xp.Good; lblSrc.Text = "Fonte: " + r.Source + "."; }
            else { lblSrc.ForeColor = Xp.Bad; lblSrc.Text = "Não consegui ler: " + r.Note + ". Alternativa: baixe o CPU-Z (versão ZIP), extraia ao lado do programa ou use \"Indicar CPU-Z\"; o programa o usa só para ler os timings."; }
            Recalc();
        }

        void Recalc()
        {
            string v; double cl;
            if (speed > 0 && tim.TryGetValue("tCAS", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out cl))
                lblLat.Text = "Latência real do CAS: " + (cl * 2000.0 / speed).ToString("0.00") + " ns  (tCAS x 2000 / MT/s)";
            else lblLat.Text = "";
        }

        // ---------- modulos ----------
        public void LoadAsync()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<RamMod> mods = RamInfo.Collect();
                try { BeginInvoke(new Action(delegate { ShowMods(mods); })); } catch (Exception) { }
            });
        }

        void ShowMods(List<RamMod> mods)
        {
            lvMod.Items.Clear();
            if (mods.Count == 0)
            {
                lvMod.Items.Add(new ListViewItem(new string[] { "Nenhum módulo informado pelo Windows (WMI)", "", "", "", "", "", "", "" }));
                return;
            }
            long total = 0; int maxConf = 0, maxSpd = 0, volt = 0, ranks = 0; bool ecc = false; string type = mods[0].Type;
            foreach (RamMod m in mods)
            {
                total += m.Cap; maxConf = Math.Max(maxConf, m.Conf); maxSpd = Math.Max(maxSpd, m.Speed); if (m.Volt > 0) volt = m.Volt; ranks += m.Rank; ecc |= m.Ecc;
                lvMod.Items.Add(new ListViewItem(new string[] { m.Slot + (m.Bank.Length > 0 ? "  (" + m.Bank + ")" : ""), (m.Cap >> 30) + " GB", m.Maker, m.Part,
                    (m.Conf > 0 ? m.Conf : m.Speed).ToString(), m.Volt > 0 ? (m.Volt / 1000.0).ToString("0.00") + " V" : "-", m.Type, m.Rank > 0 ? m.Rank.ToString() : "?" }));
            }
            bool est; channels = RamInfo.Channels(mods, out est);
            speed = maxConf > 0 ? maxConf : maxSpd;
            lblType.Text = type + "  (" + mods.Count + " módulo(s))";
            lblCap.Text = (total >> 30) + " GB";
            lblSpeed.Text = speed + " MT/s" + (maxSpd > 0 && maxSpd != speed ? "  (nominal " + maxSpd + ")" : "") + "  =  " + (speed / 2) + " MHz reais";
            lblChan.Text = RamInfo.ChannelText(channels) + (est ? "  (estimado)" : "");
            lblVolt.Text = volt > 0 ? (volt / 1000.0).ToString("0.000") + " V" : "não informado pela BIOS";
            lblRank.Text = (ranks > 0 ? ranks + " rank(s) no total" : "ranks ?") + "  /  ECC: " + (ecc ? "sim" : "não");
            lblBw.Text = "Banda teórica: " + (speed * 8.0 * channels / 1000.0).ToString("0.0") + " GB/s  (MT/s x 8 bytes x " + channels + " canal(ais)).  " +
                         (type == "DDR5" ? "DDR5: cada módulo tem 2 subcanais de 32 bits." : "");
            Recalc();
        }
    }

    // ------------------------------------------------------------------
    //  Afinidade de processos (quais nucleos cada programa pode usar)
    // ------------------------------------------------------------------
    class ProcRow { public int Pid; public string Name = "", Prio = "-"; public double Cpu; public long Mem, Mask = -1; }
    class AffPreset { public string Name; public long Mask; public int Prio; }
    class AffRule { public string Proc, Preset; }

    class ProcSorter : System.Collections.IComparer
    {
        int col; bool asc;
        public ProcSorter(int c, bool a) { col = c; asc = a; }
        public int Compare(object x, object y)
        {
            ProcRow p = (ProcRow)((ListViewItem)x).Tag, q = (ProcRow)((ListViewItem)y).Tag;
            int r;
            switch (col)
            {
                case 1: r = p.Pid.CompareTo(q.Pid); break;
                case 2: r = p.Cpu.CompareTo(q.Cpu); break;
                case 3: r = p.Mem.CompareTo(q.Mem); break;
                case 4: r = p.Mask.CompareTo(q.Mask); break;
                case 5: r = string.Compare(p.Prio, q.Prio, StringComparison.OrdinalIgnoreCase); break;
                default: r = string.Compare(p.Name, q.Name, StringComparison.OrdinalIgnoreCase); break;
            }
            return asc ? r : -r;
        }
    }

    class AffinityPage : Panel
    {
        static readonly ProcessPriorityClass[] PrioMap = { ProcessPriorityClass.Normal, ProcessPriorityClass.Idle, ProcessPriorityClass.BelowNormal, ProcessPriorityClass.Normal, ProcessPriorityClass.AboveNormal, ProcessPriorityClass.High };
        static readonly string[] Blocked = { "system", "idle", "registry", "smss", "csrss", "wininit", "services", "lsass", "winlogon", "memory compression", "secure system" };

        int N;
        ListView lvP = new ListView(), lvRules = new ListView();
        XpText txFilter; XpOption chkPause, chkSame, chkRules; XpOption[] cpuBox;
        ComboBox cbPrio, cbPre; Label lblCount, lblMsg;
        Dictionary<int, ListViewItem> pidItem = new Dictionary<int, ListViewItem>();
        List<ProcRow> lastRows = new List<ProcRow>();
        Dictionary<int, TimeSpan> prevCpu = new Dictionary<int, TimeSpan>(); DateTime prevT = DateTime.UtcNow;
        List<AffPreset> presets = new List<AffPreset>(); List<AffRule> rules = new List<AffRule>();
        HashSet<int> applied = new HashSet<int>();
        object lk = new object();
        volatile string pendingLog;
        bool busy, loadingPre; int sortCol; bool sortAsc = true;
        Timer timer = new Timer();
        LTip tip = new LTip();

        public AffinityPage()
        {
            Dock = DockStyle.Fill;
            N = Math.Max(1, Math.Min(62, Environment.ProcessorCount));
            tip.InitialDelay = 300; tip.AutoPopDelay = 9000;
            UI.Lbl(this, "Filtrar:", 0, 6, Xp.UiBold);
            txFilter = UI.Txt(this, 50, 2, 150, "");
            txFilter.Box.TextChanged += delegate { pidItem.Clear(); lvP.Items.Clear(); Fill(lastRows); };
            UI.Btn(this, "Atualizar", 208, 1, 70, delegate { Refresh2(); });
            UI.Btn(this, "Marcar mesmo nome", 284, 1, 130, delegate { MarkSameName(); });
            UI.Btn(this, "Desmarcar", 420, 1, 80, delegate { foreach (ListViewItem it in lvP.Items) it.Checked = false; });
            chkPause = UI.Opt(this, "Pausar atualizacao", 510, 4, 160, false);

            lvP.SetBounds(0, 30, 676, 226); lvP.View = View.Details; lvP.CheckBoxes = true; lvP.FullRowSelect = true; lvP.GridLines = true;
            lvP.MultiSelect = true; lvP.HideSelection = false; lvP.BackColor = Xp.EditBg; lvP.Font = Xp.Ui;
            lvP.Columns.Add("Processo", 170); lvP.Columns.Add("PID", 55); lvP.Columns.Add("CPU", 55); lvP.Columns.Add("Memoria", 75);
            lvP.Columns.Add("Afinidade (CPUs)", 170); lvP.Columns.Add("Prioridade", 110);
            UI.DoubleBuffer(lvP);
            lvP.ColumnClick += delegate(object o, ColumnClickEventArgs e)
            {
                if (sortCol == e.Column) sortAsc = !sortAsc; else { sortCol = e.Column; sortAsc = e.Column == 0; }
                lvP.ListViewItemSorter = new ProcSorter(sortCol, sortAsc); lvP.Sort();
            };
            lvP.ListViewItemSorter = new ProcSorter(0, true);
            Controls.Add(lvP);
            tip.SetToolTip(lvP, "Marque (caixinha) os processos que quer alterar. Sem nenhum marcado, vale a selecao com Ctrl/Shift.");

            BuildCores(); BuildPresets(); BuildRules();
            LoadPresets(); LoadRules();
            timer.Interval = 2500;
            timer.Tick += delegate { if ((Visible && !chkPause.Checked) || chkRules.Checked) Refresh2(); };
            timer.Start();
            VisibleChanged += delegate { if (Visible) Refresh2(); };
        }

        public void Shutdown() { timer.Stop(); }

        // ---------- interface ----------
        void BuildCores()
        {
            XpGroup g = new XpGroup(); g.Title = "Nucleos (CPUs logicos) que os processos marcados podem usar"; g.SetBounds(0, 262, 420, 324);
            Panel gp = new Panel(); gp.SetBounds(10, 22, 400, 108); gp.AutoScroll = true; g.Controls.Add(gp);
            cpuBox = new XpOption[N];
            for (int i = 0; i < N; i++)
            {
                XpOption o = new XpOption(); o.Text = "CPU " + i; o.SetBounds((i % 6) * 64, (i / 6) * 22, 62, 18); o.Checked = true;
                o.CheckedChanged += delegate { UpdateCount(); };
                gp.Controls.Add(o); cpuBox[i] = o;
            }
            UI.Btn(g, "Todos", 10, 136, 60, delegate { SetMask((1L << N) - 1); });
            UI.Btn(g, "Nenhum", 74, 136, 60, delegate { SetMask(0); });
            UI.Btn(g, "Fisicos", 138, 136, 64, delegate { SetMask(PhysMask(0)); });
            UI.Btn(g, "2a thread", 206, 136, 64, delegate { SetMask(PhysMask(1)); });
            UI.Btn(g, "1a metade", 274, 136, 64, delegate { SetMask(((1L << (N / 2)) - 1)); });
            UI.Btn(g, "2a metade", 342, 136, 64, delegate { SetMask(((1L << N) - 1) & ~((1L << (N / 2)) - 1)); });
            UI.Btn(g, "Inverter", 10, 162, 60, delegate { SetMask(~GridMask() & ((1L << N) - 1)); });
            lblCount = UI.Lbl(g, "", 80, 166, Xp.UiBold, Xp.GroupText);
            UI.Lbl(g, "Prioridade:", 10, 198);
            cbPrio = new ComboBox(); cbPrio.DropDownStyle = ComboBoxStyle.DropDownList; cbPrio.Font = Xp.Ui; cbPrio.SetBounds(84, 194, 170, 22);
            cbPrio.Items.AddRange(new object[] { "Manter a atual", "Baixa (Idle)", "Abaixo do normal", "Normal", "Acima do normal", "Alta" });
            cbPrio.SelectedIndex = 0; g.Controls.Add(cbPrio);
            chkSame = UI.Opt(g, "Aplicar tambem a TODOS os processos com o mesmo nome", 10, 222, 390, false);
            UI.Btn(g, "Aplicar aos marcados", 10, 250, 150, delegate { ApplyNow(false); }).Height = 26;
            UI.Btn(g, "Restaurar padrao", 168, 250, 130, delegate { ApplyNow(true); }).Height = 26;
            lblMsg = UI.Lbl(g, "", 10, 284, Xp.UiBold, Xp.Good);
            lblMsg.AutoSize = false; lblMsg.SetBounds(10, 282, 400, 36);
            tip.SetToolTip(chkSame, "Navegadores e jogos criam varios processos com o mesmo nome; isto altera todos de uma vez.");
            Controls.Add(g);
            UpdateCount();
        }

        void BuildPresets()
        {
            XpGroup g = new XpGroup(); g.Title = "Presets"; g.SetBounds(428, 262, 248, 150);
            cbPre = new ComboBox(); cbPre.DropDownStyle = ComboBoxStyle.DropDownList; cbPre.Font = Xp.Ui; cbPre.SetBounds(10, 24, 228, 22); g.Controls.Add(cbPre);
            cbPre.SelectedIndexChanged += delegate { PresetToGrid(); };
            UI.Btn(g, "Salvar atual", 10, 54, 100, delegate { SavePreset(); });
            UI.Btn(g, "Excluir", 116, 54, 70, delegate { DeletePreset(); });
            Label h = UI.Lbl(g, "Escolha um preset para carregar nucleos e prioridade; depois clique em \"Aplicar aos marcados\". \"Salvar atual\" guarda o que esta na grade.", 10, 84, Xp.Ui, Xp.Dim);
            h.MaximumSize = new Size(230, 0);
            Controls.Add(g);
        }

        void BuildRules()
        {
            XpGroup g = new XpGroup(); g.Title = "Regras automaticas"; g.SetBounds(428, 418, 248, 168);
            chkRules = UI.Opt(g, "Aplicar regras automaticamente", 10, 22, 228, false);
            lvRules.SetBounds(10, 46, 228, 80); lvRules.View = View.Details; lvRules.FullRowSelect = true; lvRules.GridLines = true; lvRules.Font = Xp.Ui; lvRules.BackColor = Xp.EditBg;
            lvRules.Columns.Add("Processo", 110); lvRules.Columns.Add("Preset", 100);
            g.Controls.Add(lvRules);
            UI.Btn(g, "Nova regra", 10, 132, 100, delegate { NewRule(); });
            UI.Btn(g, "Remover", 116, 132, 80, delegate { RemoveRule(); });
            chkRules.CheckedChanged += delegate { Settings.Set("aff.rulesOn", chkRules.Checked ? "1" : "0"); };
            tip.SetToolTip(chkRules, "Quando o processo abrir, o preset e aplicado sozinho (ex.: jogo.exe -> nucleos fisicos).");
            Controls.Add(g);
        }

        // ---------- grade de nucleos ----------
        long GridMask() { long m = 0; for (int i = 0; i < N; i++) if (cpuBox[i].Checked) m |= 1L << i; return m; }
        void SetMask(long m) { for (int i = 0; i < N; i++) cpuBox[i].Checked = ((m >> i) & 1) != 0; UpdateCount(); }
        long PhysMask(int off)
        {
            bool smt = CpuId.Threads > CpuId.Cores && CpuId.Cores > 0;
            if (!smt) return off == 0 ? (1L << N) - 1 : 0;
            long m = 0; for (int i = off; i < N; i += 2) m |= 1L << i;
            return m;
        }
        void UpdateCount() { int c = 0; for (int i = 0; i < N; i++) if (cpuBox[i].Checked) c++; lblCount.Text = c + Lang.Pick(" de ", " of ") + N + " CPUs marcados"; }

        string MaskText(long mask)
        {
            if (mask == -1) return "(negado)";
            long all = (1L << N) - 1;
            if ((mask & all) == all) return "Todos (" + N + ")";
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < N)
            {
                if (((mask >> i) & 1) != 0)
                {
                    int j = i;
                    while (j + 1 < N && ((mask >> (j + 1)) & 1) != 0) j++;
                    if (sb.Length > 0) sb.Append(",");
                    sb.Append(i == j ? i.ToString() : i + "-" + j);
                    i = j + 1;
                }
                else i++;
            }
            return sb.Length == 0 ? "-" : sb.ToString();
        }

        void Msg(string t, bool err) { lblMsg.ForeColor = err ? Xp.Bad : Xp.Good; lblMsg.Text = t; }

        // ---------- lista de processos ----------
        void Refresh2()
        {
            if (busy) return;
            busy = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { Collect(); } catch (Exception) { }
                finally { busy = false; }
            });
        }

        void Collect()
        {
            List<ProcRow> rows = new List<ProcRow>();
            DateTime now = DateTime.UtcNow;
            double wall = (now - prevT).TotalSeconds; if (wall <= 0.2) wall = 1;
            Dictionary<int, TimeSpan> cur = new Dictionary<int, TimeSpan>();
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    ProcRow r = new ProcRow(); r.Pid = p.Id; r.Name = p.ProcessName;
                    try { r.Mem = p.WorkingSet64; } catch (Exception) { }
                    try { r.Mask = (long)p.ProcessorAffinity; } catch (Exception) { r.Mask = -1; }
                    try { r.Prio = p.PriorityClass.ToString(); } catch (Exception) { r.Prio = "-"; }
                    try
                    {
                        TimeSpan t = p.TotalProcessorTime; cur[r.Pid] = t; TimeSpan pt;
                        if (prevCpu.TryGetValue(r.Pid, out pt)) r.Cpu = (t - pt).TotalSeconds / wall / Environment.ProcessorCount * 100.0;
                    }
                    catch (Exception) { }
                    rows.Add(r);
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
            prevCpu = cur; prevT = now;
            if (chkRulesOn) ApplyRules(rows);
            if (Visible) { try { BeginInvoke(new Action(delegate { Fill(rows); })); } catch (Exception) { } }
        }
        bool chkRulesOn { get { return Settings.Get("aff.rulesOn", "0") == "1"; } }

        void Fill(List<ProcRow> rows)
        {
            lastRows = rows;
            string f = txFilter.Text.Trim().ToLower();
            HashSet<int> alive = new HashSet<int>();
            lvP.BeginUpdate();
            foreach (ProcRow r in rows)
            {
                if (f.Length > 0 && r.Name.ToLower().IndexOf(f) < 0) continue;
                alive.Add(r.Pid);
                string[] t = { r.Name, r.Pid.ToString(), r.Cpu.ToString("0.0") + "%", (r.Mem >> 20) + " MB", Lang.T(MaskText(r.Mask)), Lang.T(r.Prio) };
                ListViewItem it;
                if (!pidItem.TryGetValue(r.Pid, out it)) { it = new ListViewItem(t); it.Tag = r; pidItem[r.Pid] = it; lvP.Items.Add(it); }
                else
                {
                    it.Tag = r;
                    for (int i = 0; i < t.Length; i++) if (it.SubItems[i].Text != t[i]) it.SubItems[i].Text = t[i];
                }
            }
            List<int> gone = new List<int>();
            foreach (KeyValuePair<int, ListViewItem> kv in pidItem) if (!alive.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (int pid in gone) { lvP.Items.Remove(pidItem[pid]); pidItem.Remove(pid); }
            lvP.EndUpdate();
            lvP.Sort();
            string log = pendingLog;
            if (log != null) { pendingLog = null; Msg(log, false); }
        }

        void MarkSameName()
        {
            HashSet<string> names = new HashSet<string>();
            foreach (ListViewItem it in lvP.SelectedItems) names.Add(((ProcRow)it.Tag).Name.ToLower());
            foreach (ListViewItem it in lvP.CheckedItems) names.Add(((ProcRow)it.Tag).Name.ToLower());
            foreach (ListViewItem it in lvP.Items) if (names.Contains(((ProcRow)it.Tag).Name.ToLower())) it.Checked = true;
        }

        // ---------- aplicar ----------
        static bool IsBlocked(string name) { return Array.IndexOf(Blocked, name.ToLower()) >= 0; }

        static bool ApplyTo(int pid, long mask, int prioIdx, out string err)
        {
            err = null;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    p.ProcessorAffinity = (IntPtr)mask;
                    if (prioIdx > 0) p.PriorityClass = PrioMap[prioIdx];
                }
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        List<ProcRow> Targets()
        {
            List<ProcRow> l = new List<ProcRow>();
            if (lvP.CheckedItems.Count > 0) foreach (ListViewItem it in lvP.CheckedItems) l.Add((ProcRow)it.Tag);
            else foreach (ListViewItem it in lvP.SelectedItems) l.Add((ProcRow)it.Tag);
            if (chkSame.Checked)
            {
                HashSet<string> names = new HashSet<string>(); HashSet<int> have = new HashSet<int>();
                foreach (ProcRow r in l) { names.Add(r.Name.ToLower()); have.Add(r.Pid); }
                foreach (ProcRow r in lastRows) if (names.Contains(r.Name.ToLower()) && have.Add(r.Pid)) l.Add(r);
            }
            return l;
        }

        void ApplyNow(bool restore)
        {
            long mask = restore ? (1L << N) - 1 : GridMask();
            int prio = restore ? 3 : cbPrio.SelectedIndex;
            if (mask == 0) { Msg("Marque pelo menos um CPU na grade.", true); return; }
            List<ProcRow> t = Targets();
            if (t.Count == 0) { Msg("Marque (caixinha) ou selecione pelo menos um processo na lista.", true); return; }
            int ok = 0, skip = 0, fail = 0; string err = "";
            foreach (ProcRow r in t)
            {
                if (IsBlocked(r.Name)) { skip++; continue; }
                string e;
                if (ApplyTo(r.Pid, mask, prio, out e)) ok++; else { fail++; if (err.Length == 0) err = r.Name + ": " + e; }
            }
            Msg(ok + " processo(s) alterado(s)" + (skip > 0 ? ", " + skip + " do sistema ignorado(s)" : "") + (fail > 0 ? ", " + fail + " falhou (" + err + ")" : "") + ".", ok == 0);
            Refresh2();
        }

        // ---------- presets ----------
        void LoadPresets()
        {
            presets.Clear();
            foreach (string e in Settings.Get("aff.presets", "").Split(new string[] { "~|~" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = e.Split(new string[] { "~=~" }, StringSplitOptions.None);
                long m; int pr;
                if (p.Length == 3 && long.TryParse(p[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out m) && int.TryParse(p[2], out pr))
                { AffPreset a = new AffPreset(); a.Name = p[0]; a.Mask = m; a.Prio = pr; presets.Add(a); }
            }
            RefreshPresetCombo(null);
        }
        void SavePresets()
        {
            List<string> l = new List<string>();
            foreach (AffPreset a in presets) l.Add(a.Name + "~=~" + a.Mask.ToString("X") + "~=~" + a.Prio);
            Settings.Set("aff.presets", string.Join("~|~", l.ToArray()));
        }
        void RefreshPresetCombo(string select)
        {
            loadingPre = true;
            cbPre.Items.Clear();
            foreach (AffPreset a in presets) cbPre.Items.Add(a.Name);
            if (select != null) cbPre.SelectedItem = select;
            loadingPre = false;
        }
        void PresetToGrid()
        {
            if (loadingPre || cbPre.SelectedItem == null) return;
            foreach (AffPreset a in presets)
                if (a.Name == (string)cbPre.SelectedItem) { SetMask(a.Mask); cbPrio.SelectedIndex = Math.Max(0, Math.Min(5, a.Prio)); }
        }
        void SavePreset()
        {
            long m = GridMask();
            if (m == 0) { Msg("Marque pelo menos um CPU antes de salvar.", true); return; }
            string n = PromptForm.Ask(FindForm(), "Preset", "Nome do preset (ex.: Jogo - nucleos fisicos):", cbPre.Text);
            if (string.IsNullOrEmpty(n)) return;
            AffPreset ex = null; foreach (AffPreset a in presets) if (a.Name == n) ex = a;
            if (ex == null) { ex = new AffPreset(); ex.Name = n; presets.Add(ex); }
            ex.Mask = m; ex.Prio = cbPrio.SelectedIndex;
            SavePresets(); RefreshPresetCombo(n);
            Msg("Preset \"" + n + "\" salvo.", false);
        }
        void DeletePreset()
        {
            if (cbPre.SelectedItem == null) return;
            string n = (string)cbPre.SelectedItem;
            presets.RemoveAll(delegate(AffPreset a) { return a.Name == n; });
            SavePresets(); RefreshPresetCombo(null);
        }

        // ---------- regras automaticas ----------
        void LoadRules()
        {
            lock (lk)
            {
                rules.Clear();
                foreach (string e in Settings.Get("aff.rules", "").Split(new string[] { "~|~" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] p = e.Split(new string[] { "~=~" }, StringSplitOptions.None);
                    if (p.Length == 2) { AffRule r = new AffRule(); r.Proc = p[0]; r.Preset = p[1]; rules.Add(r); }
                }
            }
            chkRules.Checked = chkRulesOn;
            ShowRules();
        }
        void ShowRules()
        {
            lvRules.Items.Clear();
            lock (lk) foreach (AffRule r in rules) lvRules.Items.Add(new ListViewItem(new string[] { r.Proc, r.Preset }));
        }
        void SaveRules()
        {
            List<string> l = new List<string>();
            lock (lk) foreach (AffRule r in rules) l.Add(r.Proc + "~=~" + r.Preset);
            Settings.Set("aff.rules", string.Join("~|~", l.ToArray()));
        }
        void NewRule()
        {
            if (cbPre.SelectedItem == null) { Msg("Escolha (ou salve) um preset antes de criar a regra.", true); return; }
            string def = lvP.SelectedItems.Count > 0 ? ((ProcRow)lvP.SelectedItems[0].Tag).Name + ".exe" : "";
            string n = PromptForm.Ask(FindForm(), "Nova regra", "Nome do processo (ex.: game.exe). Preset: " + cbPre.SelectedItem, def);
            if (string.IsNullOrEmpty(n)) return;
            AffRule r = new AffRule(); r.Proc = n.Trim(); r.Preset = (string)cbPre.SelectedItem;
            lock (lk) rules.Add(r);
            SaveRules(); ShowRules();
        }
        void RemoveRule()
        {
            if (lvRules.SelectedItems.Count == 0) return;
            int i = lvRules.SelectedIndices[0];
            lock (lk) if (i < rules.Count) rules.RemoveAt(i);
            SaveRules(); ShowRules();
        }
        void ApplyRules(List<ProcRow> rows)
        {
            AffRule[] rs; AffPreset[] ps;
            lock (lk) { rs = rules.ToArray(); ps = presets.ToArray(); }
            if (rs.Length == 0) return;
            HashSet<int> alive = new HashSet<int>();
            foreach (ProcRow r in rows) alive.Add(r.Pid);
            applied.RemoveWhere(delegate(int pid) { return !alive.Contains(pid); });
            foreach (ProcRow r in rows)
            {
                if (applied.Contains(r.Pid) || IsBlocked(r.Name)) continue;
                foreach (AffRule rule in rs)
                {
                    string pn = rule.Proc.ToLower(); if (pn.EndsWith(".exe")) pn = pn.Substring(0, pn.Length - 4);
                    if (pn != r.Name.ToLower()) continue;
                    foreach (AffPreset a in ps)
                        if (a.Name == rule.Preset)
                        {
                            string e;
                            if (ApplyTo(r.Pid, a.Mask & ((1L << N) - 1), a.Prio, out e)) pendingLog = "Regra aplicada: " + r.Name + " (PID " + r.Pid + ") -> " + a.Name;
                            applied.Add(r.Pid);
                        }
                }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Helpers de interface
    // ------------------------------------------------------------------
    // ------------------------------------------------------------------
    //  Desinstalador: remove o programa e os restos (pastas, registro, inicializacao, servicos, tarefas)
    // ------------------------------------------------------------------
    class UProg
    {
        public string Name = "", Publisher = "", Version = "", Loc = "", Uninst = "", KeyFull = "", Date = "";
        public long SizeKb;
        public bool Manual;
    }
    class ULeft
    {
        public string Kind = "", Target = "", Extra = "", Why = "";
        public long Size = -1;
        public bool Strong;
    }

    class UninstallPage : Panel
    {
        static readonly string[] Generic = { "windows", "microsoft", "system", "common", "program", "programs", "update", "updates", "software", "data", "temp", "cache", "setup", "install", "installer", "drivers", "driver", "intel", "amd", "nvidia", "google", "apps", "app", "files", "microsoftedge", "internet", "explorer", "defender", "visual", "runtime" };
        ListView lvP = new ListView(), lvL = new ListView();
        XpText txFind; Label lblInfo, lblMsg, lblSel; XpProgress pb;
        XpOption chkBin, chkReg, chkRestore;
        XpButton bUn, bForce, bScan, bDel, bKill;
        LTip tipU = new LTip();
        UProg lastProg; static int rebootN;
        List<UProg> progs = new List<UProg>();
        UProg sel; bool busy; int sortCol; bool sortAsc = true;

        public UninstallPage()
        {
            Dock = DockStyle.Fill;
            XpGroup g1 = new XpGroup(); g1.Title = "1. Escolha o programa que quer remover"; g1.SetBounds(0, 0, 676, 262);
            UI.Lbl(g1, "Buscar:", 14, 30, Xp.UiBold);
            txFind = UI.Txt(g1, 66, 26, 200, "");
            txFind.Box.TextChanged += delegate { FillProgs(); };
            UI.Btn(g1, "Atualizar lista", 276, 25, 100, delegate { LoadProgs(); });
            UI.Btn(g1, "Escolher pasta ou .exe...", 382, 25, 160, delegate { PickManual(); });
            lblSel = UI.Lbl(g1, "", 548, 30, Xp.Ui, Xp.Dim);
            lvP.SetBounds(12, 56, 652, 156); lvP.View = View.Details; lvP.FullRowSelect = true; lvP.GridLines = true; lvP.HideSelection = false; lvP.MultiSelect = false;
            lvP.BackColor = Xp.EditBg; lvP.Font = Xp.Ui;
            lvP.Columns.Add("Programa", 250); lvP.Columns.Add("Editora", 130); lvP.Columns.Add("Versao", 80); lvP.Columns.Add("Tamanho", 70); lvP.Columns.Add("Instalado", 80);
            UI.DoubleBuffer(lvP);
            lvP.ColumnClick += delegate(object o, ColumnClickEventArgs e) { if (sortCol == e.Column) sortAsc = !sortAsc; else { sortCol = e.Column; sortAsc = true; } FillProgs(); };
            lvP.SelectedIndexChanged += delegate
            {
                sel = lvP.SelectedItems.Count > 0 ? lvP.SelectedItems[0].Tag as UProg : null;
                lblSel.Text = sel == null ? "" : "Selecionado";
                UpdateButtons();
            };
            lvP.DoubleClick += delegate { if (sel != null) DoUninstall(false); };
            g1.Controls.Add(lvP);
            bUn = UI.Btn(g1, "Desinstalar e limpar restos", 12, 222, 178, delegate { DoUninstall(false); });
            bForce = UI.Btn(g1, "Remocao forcada", 194, 222, 112, delegate { DoUninstall(true); });
            bScan = UI.Btn(g1, "So procurar restos", 310, 222, 112, delegate { StartScan(sel, "Procurando restos..."); });
            bKill = UI.Btn(g1, "Encerrar processos", 426, 222, 118, delegate { KillNow(); });
            bUn.Height = bForce.Height = bScan.Height = bKill.Height = 28;
            tipU.SetToolTip(bKill, "Finaliza todos os programas abertos que rodam da pasta do programa selecionado (e para os servicos dele). Use antes de desinstalar se ele estiver aberto.");
            lblInfo = UI.Lbl(g1, "Duplo clique desinstala.", 550, 228, Xp.Ui, Xp.Dim);
            lblInfo.AutoSize = false; lblInfo.SetBounds(550, 218, 114, 40);
            Controls.Add(g1);

            XpGroup g2 = new XpGroup(); g2.Title = "2. Restos encontrados (revise e remova)"; g2.SetBounds(0, 270, 676, 258);
            lvL.SetBounds(12, 26, 652, 148); lvL.View = View.Details; lvL.CheckBoxes = true; lvL.FullRowSelect = true; lvL.GridLines = true; lvL.HideSelection = false;
            lvL.BackColor = Xp.EditBg; lvL.Font = Xp.Ui;
            lvL.Columns.Add("Tipo", 90); lvL.Columns.Add("Local", 380); lvL.Columns.Add("Tamanho", 70); lvL.Columns.Add("Certeza", 60);
            UI.DoubleBuffer(lvL);
            g2.Controls.Add(lvL);
            chkBin = UI.Opt(g2, "Pastas e arquivos vao para a Lixeira (podem ser recuperados)", 12, 182, 400, false); chkBin.Checked = true;
            chkReg = UI.Opt(g2, "Salvar copia (.reg) das chaves do registro antes de apagar", 12, 202, 400, false); chkReg.Checked = true;
            chkRestore = UI.Opt(g2, "Criar ponto de restauracao do Windows antes", 12, 222, 400, false);
            UI.Btn(g2, "Marcar tudo", 420, 180, 110, delegate { foreach (ListViewItem it in lvL.Items) it.Checked = true; });
            UI.Btn(g2, "Desmarcar tudo", 536, 180, 128, delegate { foreach (ListViewItem it in lvL.Items) it.Checked = false; });
            bDel = UI.Btn(g2, "Remover itens marcados", 420, 214, 244, delegate { DoRemove(); });
            bDel.Height = 30;
            Controls.Add(g2);

            pb = new XpProgress(); pb.SetBounds(0, 534, 676, 14); Controls.Add(pb);
            lblMsg = UI.Lbl(this, "Dica: itens com 'Certeza: alta' sao ligados ao programa; os de certeza 'media' vem desmarcados - confira antes.", 0, 552, Xp.Ui, Xp.Dim);
            lblMsg.AutoSize = false; lblMsg.SetBounds(0, 550, 676, 34);
            UpdateButtons();
            Timer tk = new Timer(); tk.Interval = 100; tk.Tick += delegate { if (busy) pb.Step(); }; tk.Start();
            VisibleChanged += delegate { if (Visible && progs.Count == 0 && !busy) LoadProgs(); };
        }

        void UpdateButtons()
        {
            bool ok = sel != null && !busy;
            bUn.Enabled = ok && !sel.Manual && sel.Uninst.Length > 0;
            bForce.Enabled = ok; bScan.Enabled = ok; bKill.Enabled = ok; bDel.Enabled = !busy;
        }
        void Msg(string t) { lblMsg.Text = t; }
        void Busy(bool b, string t)
        {
            busy = b; pb.Marquee = b; pb.Value = 0; if (t != null) Msg(t);
            UpdateButtons();
        }

        // ----- lista de programas instalados -----
        void LoadProgs()
        {
            Busy(true, "Lendo a lista de programas instalados...");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<UProg> l = new List<UProg>();
                try
                {
                    ReadUninstall(Registry.LocalMachine, "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", l);
                    ReadUninstall(Registry.LocalMachine, "HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", l);
                    ReadUninstall(Registry.CurrentUser, "HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", l);
                }
                catch (Exception) { }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        List<UProg> manual = progs.FindAll(delegate(UProg x) { return x.Manual; });
                        progs = l; progs.AddRange(manual);
                        FillProgs(); Busy(false, "Pronto: " + l.Count + " programas. Escolha um na lista.");
                    }));
                }
                catch (Exception) { }
            });
        }
        static string V(RegistryKey k, string n) { try { object o = k.GetValue(n); return o == null ? "" : o.ToString().Trim(); } catch (Exception) { return ""; } }
        static void ReadUninstall(RegistryKey root, string hive, string path, List<UProg> into)
        {
            using (RegistryKey rk = root.OpenSubKey(path))
            {
                if (rk == null) return;
                foreach (string sn in rk.GetSubKeyNames())
                {
                    try
                    {
                        using (RegistryKey k = rk.OpenSubKey(sn))
                        {
                            if (k == null) continue;
                            string name = V(k, "DisplayName");
                            if (name.Length == 0 || V(k, "SystemComponent") == "1" || V(k, "ParentKeyName").Length > 0) continue;
                            if (V(k, "ReleaseType") == "Update" || V(k, "ReleaseType") == "Hotfix") continue;
                            UProg p = new UProg();
                            p.Name = name; p.Publisher = V(k, "Publisher"); p.Version = V(k, "DisplayVersion");
                            p.Loc = V(k, "InstallLocation").Trim('"').TrimEnd('\\');
                            p.Uninst = V(k, "UninstallString"); p.KeyFull = hive + "\\" + path + "\\" + sn;
                            long kb; if (long.TryParse(V(k, "EstimatedSize"), out kb)) p.SizeKb = kb;
                            string d = V(k, "InstallDate");
                            if (d.Length == 8) p.Date = d.Substring(6, 2) + "/" + d.Substring(4, 2) + "/" + d.Substring(0, 4);
                            if (p.Loc.Length == 0)
                            {
                                string ico = V(k, "DisplayIcon").Trim('"');
                                int c = ico.IndexOf(','); if (c > 0) ico = ico.Substring(0, c);
                                try { if (ico.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(ico)) p.Loc = Path.GetDirectoryName(ico); } catch (Exception) { }
                            }
                            into.Add(p);
                        }
                    }
                    catch (Exception) { }
                }
            }
        }
        void FillProgs()
        {
            string f = txFind.Box.Text.Trim().ToLower();
            List<UProg> l = progs.FindAll(delegate(UProg x) { return f.Length == 0 || x.Name.ToLower().Contains(f) || x.Publisher.ToLower().Contains(f); });
            int sc = sortCol; bool asc = sortAsc;
            l.Sort(delegate(UProg a, UProg b)
            {
                int r;
                if (sc == 3) r = a.SizeKb.CompareTo(b.SizeKb);
                else if (sc == 4) r = DateKey(a.Date).CompareTo(DateKey(b.Date));
                else
                {
                    string x = sc == 1 ? a.Publisher : sc == 2 ? a.Version : a.Name, y = sc == 1 ? b.Publisher : sc == 2 ? b.Version : b.Name;
                    r = string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase);
                }
                return asc ? r : -r;
            });
            string keep = sel != null ? sel.Name : null;
            lvP.BeginUpdate(); lvP.Items.Clear();
            foreach (UProg p in l)
            {
                ListViewItem it = new ListViewItem(p.Name);
                it.SubItems.Add(p.Publisher); it.SubItems.Add(p.Version);
                it.SubItems.Add(p.SizeKb > 0 ? (p.SizeKb >= 1024 ? (p.SizeKb / 1024.0).ToString("0.#") + " MB" : p.SizeKb + " KB") : "");
                it.SubItems.Add(p.Date);
                it.Tag = p;
                if (p.Manual) it.ForeColor = Xp.Good;
                lvP.Items.Add(it);
                if (keep != null && p.Name == keep) { it.Selected = true; }
            }
            lvP.EndUpdate();
        }
        static string DateKey(string d) { return d.Length == 10 ? d.Substring(6, 4) + d.Substring(3, 2) + d.Substring(0, 2) : ""; }

        void PickManual()
        {
            string path = null;
            int r = AskPick();
            if (r == 0) return;
            if (r == 1)
            {
                using (FolderBrowserDialog fd = new FolderBrowserDialog())
                {
                    fd.Description = Lang.T("Escolha a pasta do programa");
                    if (fd.ShowDialog(this) != DialogResult.OK) return;
                    path = fd.SelectedPath;
                }
            }
            else
            {
                using (OpenFileDialog od = new OpenFileDialog())
                {
                    od.Filter = Lang.T("Programas (*.exe)|*.exe|Todos|*.*");
                    if (od.ShowDialog(this) != DialogResult.OK) return;
                    path = od.FileName;
                }
            }
            UProg p = new UProg(); p.Manual = true;
            string folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            string nm = Directory.Exists(path) ? new DirectoryInfo(path).Name : Path.GetFileNameWithoutExtension(path);
            try { if (File.Exists(path)) { FileVersionInfo vi = FileVersionInfo.GetVersionInfo(path); if (!string.IsNullOrEmpty(vi.ProductName)) nm = vi.ProductName.Trim(); if (!string.IsNullOrEmpty(vi.CompanyName)) p.Publisher = vi.CompanyName.Trim(); } } catch (Exception) { }
            p.Name = nm; p.Loc = folder.TrimEnd('\\'); p.Version = "(manual)";
            if (!SafeFolder(p.Loc)) { Dlg.Show("Essa pasta e protegida pelo sistema e nao pode ser usada aqui:\n" + p.Loc, "BunoTunner", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            progs.Insert(0, p);
            sortCol = -1; txFind.Box.Text = ""; FillProgs();
            if (lvP.Items.Count > 0) { sel = null; foreach (ListViewItem it in lvP.Items) if (it.Tag == p) { it.Selected = true; it.EnsureVisible(); break; } }
            Msg("Adicionado: " + p.Name + ". Use 'Remocao forcada' ou 'So procurar restos'.");
        }

        // pergunta com botoes Pasta / Exe / Cancelar: 1 = pasta, 2 = exe, 0 = cancelar
        int AskPick()
        {
            int res = 0;
            using (Form f = new Form())
            {
                f.Text = "BunoTunner - Adicionar manualmente"; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = false; f.ClientSize = new Size(380, 120); f.BackColor = Xp.Face; f.Font = Xp.Ui;
                Label l = new Label(); l.Text = "Use isto para programas que nao aparecem na lista (portateis, restos de algo ja removido, etc.).\n\nO que voce quer escolher?";
                l.SetBounds(14, 12, 352, 56); l.ForeColor = Xp.Text; f.Controls.Add(l);
                XpButton bp = new XpButton(); bp.Text = "Pasta"; bp.SetBounds(30, 80, 100, 28); bp.Click += delegate { res = 1; f.Close(); }; f.Controls.Add(bp);
                XpButton be = new XpButton(); be.Text = "Exe"; be.SetBounds(140, 80, 100, 28); be.Click += delegate { res = 2; f.Close(); }; f.Controls.Add(be);
                XpButton bc = new XpButton(); bc.Text = "Cancelar"; bc.SetBounds(250, 80, 100, 28); bc.Click += delegate { res = 0; f.Close(); }; f.Controls.Add(bc);
                f.CancelButton = null;
                Lang.Apply(f, null);
                f.ShowDialog(this);
            }
            return res;
        }

        // ----- nomes e seguranca -----
        static string Norm(string s) { return Regex.Replace((s ?? "").ToLower(), "[^a-z0-9]", ""); }
        static string Core(string name)
        {
            string s = Regex.Replace(name ?? "", @"\([^)]*\)|\[[^\]]*\]", " ");
            s = Regex.Replace(s, @"\b[vV]?\d+([.\-_]\d+)*\b", " ");
            s = Regex.Replace(s, @"\b(x64|x86|64-bit|32-bit|64bit|32bit|edition|version|versao)\b", " ", RegexOptions.IgnoreCase);
            return Regex.Replace(s, @"\s+", " ").Trim();
        }
        static bool WeakName(string nc)
        {
            if (nc.Length < 4) return true;
            foreach (string g in Generic) if (nc == g) return true;
            return false;
        }
        static string[] ProtectedDirs()
        {
            List<string> l = new List<string>();
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.System));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
            l.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
            return l.ToArray();
        }
        // pasta valida para apagar: nao e raiz de disco, nem pasta do sistema, nem uma das pastas-mae protegidas
        static bool SafeFolder(string p)
        {
            try
            {
                if (string.IsNullOrEmpty(p)) return false;
                string full = Path.GetFullPath(p).TrimEnd('\\');
                if (full.Length < 4 || Path.GetPathRoot(full).TrimEnd('\\').Equals(full, StringComparison.OrdinalIgnoreCase)) return false;
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (full.StartsWith(win, StringComparison.OrdinalIgnoreCase)) return false;
                foreach (string d in ProtectedDirs())
                    if (!string.IsNullOrEmpty(d) && full.Equals(d.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
                string[] parts = full.Split('\\');
                return parts.Length >= 3;
            }
            catch (Exception) { return false; }
        }
        static bool SafeFile(string p)
        {
            try
            {
                string full = Path.GetFullPath(p);
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                return !full.StartsWith(win, StringComparison.OrdinalIgnoreCase) && Path.GetPathRoot(full).TrimEnd('\\').Length < full.TrimEnd('\\').Length;
            }
            catch (Exception) { return false; }
        }

        // ----- desinstalar -----
        void DoUninstall(bool force)
        {
            if (sel == null || busy) return;
            UProg p = sel;
            if (!force)
            {
                if (p.Uninst.Length == 0) { Msg("Este item nao tem desinstalador. Use 'Remocao forcada'."); return; }
                if (Dlg.Show("Desinstalar \"" + p.Name + "\"?\n\n1) O desinstalador oficial sera aberto (siga as telas dele).\n2) Depois o BunoTunner procura o que sobrou para voce revisar.", "BunoTunner - Desinstalar", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                if (chkRestore.Checked) MakeRestorePoint(p.Name);
                Busy(true, "Desinstalador aberto: conclua as telas dele... (a busca de restos comeca quando ele terminar)");
                string cmd = p.Uninst;
                if (Regex.IsMatch(cmd, @"msiexec", RegexOptions.IgnoreCase)) cmd = Regex.Replace(cmd, @"/I(\s*\{)", "/X$1", RegexOptions.IgnoreCase);
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    string err = null;
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"");
                        psi.UseShellExecute = false; psi.CreateNoWindow = true;
                        using (Process pr = Process.Start(psi)) pr.WaitForExit();
                    }
                    catch (Exception ex) { err = ex.Message; }
                    try { BeginInvoke(new Action(delegate { Busy(false, err == null ? "Desinstalador terminou." : "Falha ao abrir o desinstalador: " + err); StartScan(p, "Procurando restos de " + p.Name + "..."); })); } catch (Exception) { }
                });
            }
            else
            {
                if (Dlg.Show("REMOCAO FORCADA de \"" + p.Name + "\"\n\nIsto NAO usa o desinstalador: encerra os processos do programa, para os servicos dele e lista tudo o que for dele (pasta, registro, inicializacao, tarefas) para voce apagar.\n\nNada e apagado antes de voce revisar e confirmar a lista. Continuar?", "BunoTunner - Remocao forcada", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                if (chkRestore.Checked) MakeRestorePoint(p.Name);
                int k = KillProcs(p);
                StartScan(p, k > 0 ? k + " processo(s) encerrado(s). Procurando restos..." : "Procurando restos...");
            }
        }
        void KillNow()
        {
            if (sel == null) return;
            if (string.IsNullOrEmpty(sel.Loc) || !SafeFolder(sel.Loc)) { Msg("Nao sei qual e a pasta deste programa; use 'Escolher pasta ou .exe' para informar."); return; }
            if (Dlg.Show("Finalizar todos os processos de \"" + sel.Name + "\" (pasta " + sel.Loc + ")?\nTrabalho nao salvo nele sera perdido.", "BunoTunner", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            int n = KillProcs(sel);
            Msg(n > 0 ? n + " processo(s) finalizado(s)." : "Nenhum processo desse programa esta aberto.");
        }
        static int KillProcs(UProg p)
        {
            int n = 0;
            if (string.IsNullOrEmpty(p.Loc) || !SafeFolder(p.Loc)) return 0;
            string root = p.Loc.TrimEnd('\\') + "\\";
            try
            {
                using (RegistryKey sk = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                    if (sk != null)
                        foreach (string sn in sk.GetSubKeyNames())
                            using (RegistryKey k = sk.OpenSubKey(sn))
                                if (k != null && V(k, "ImagePath").IndexOf(p.Loc, StringComparison.OrdinalIgnoreCase) >= 0) { int c; Proc.Run("sc.exe", "stop \"" + sn + "\"", 10000, out c); }
            }
            catch (Exception) { }
            foreach (Process pr in Process.GetProcesses())
            {
                try
                {
                    string f = pr.MainModule.FileName;
                    if (f.StartsWith(root, StringComparison.OrdinalIgnoreCase) && pr.Id != Process.GetCurrentProcess().Id) { pr.Kill(); pr.WaitForExit(2000); n++; }
                }
                catch (Exception) { }
                finally { pr.Dispose(); }
            }
            return n;
        }
        void MakeRestorePoint(string name)
        {
            Busy(true, "Criando ponto de restauracao (pode levar um minuto)...");
            Update();
            int code;
            string o = Proc.Run("powershell.exe", "-NoProfile -Command \"Checkpoint-Computer -Description 'Buno Tunner - antes de remover " + name.Replace("'", "").Replace("\"", "") + "' -RestorePointType MODIFY_SETTINGS\"", 120000, out code);
            Busy(false, code == 0 ? "Ponto de restauracao criado." : "Nao foi possivel criar o ponto de restauracao (o Windows limita 1 a cada 24 h, ou a protecao do sistema esta desligada). Continuando.");
        }

        // ----- busca de restos -----
        void StartScan(UProg p, string msg)
        {
            if (p == null || busy) return;
            Busy(true, msg);
            lastProg = p;
            lvL.Items.Clear();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<ULeft> res = new List<ULeft>();
                string err = null;
                try { res = Scan(p); } catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        lvL.BeginUpdate();
                        foreach (ULeft l in res)
                        {
                            ListViewItem it = new ListViewItem(l.Kind);
                            it.SubItems.Add(l.Target + (l.Extra.Length > 0 ? "   [" + l.Extra + "]" : ""));
                            it.SubItems.Add(l.Size >= 0 ? Fmt(l.Size) : "");
                            it.SubItems.Add(l.Strong ? "alta" : "media");
                            it.Tag = l; it.Checked = l.Strong;
                            if (!l.Strong) it.ForeColor = Xp.Dim;
                            lvL.Items.Add(it);
                        }
                        lvL.EndUpdate();
                        Busy(false, err != null ? "Erro na busca: " + err : res.Count == 0 ? "Nada sobrou: nenhum resto encontrado para " + p.Name + "." : res.Count + " item(ns) encontrado(s). Revise a lista (so os de certeza alta vem marcados) e clique em 'Remover itens marcados'.");
                    }));
                }
                catch (Exception) { }
            });
        }
        static string Fmt(long b) { return b >= 1073741824 ? (b / 1073741824.0).ToString("0.0") + " GB" : b >= 1048576 ? (b / 1048576.0).ToString("0.0") + " MB" : (b / 1024) + " KB"; }
        static long DirSize(string d, ref int budget)
        {
            long t = 0;
            try
            {
                foreach (string f in Directory.GetFiles(d)) { if (--budget < 0) return t; try { t += new FileInfo(f).Length; } catch (Exception) { } }
                foreach (string s in Directory.GetDirectories(d)) { if (budget < 0) return t; t += DirSize(s, ref budget); }
            }
            catch (Exception) { }
            return t;
        }

        static bool NameHit(string fname, string nc, string np, out bool strong)
        {
            strong = false;
            string n = Norm(fname);
            if (n.Length == 0 || WeakName(nc)) return false;
            if (n == nc) { strong = true; return true; }
            if (nc.Length >= 6 && (n.StartsWith(nc) || nc.StartsWith(n) && n.Length >= 6)) return true;
            return false;
        }

        static List<ULeft> Scan(UProg p)
        {
            List<ULeft> res = new List<ULeft>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string core = Core(p.Name), nc = Norm(core), np = Norm(p.Publisher);
            string loc = (p.Loc ?? "").TrimEnd('\\');
            bool locOk = loc.Length > 0 && SafeFolder(loc);

            // pasta de instalacao
            if (locOk && Directory.Exists(loc))
            {
                int bud = 40000;
                ULeft l = new ULeft(); l.Kind = "Pasta"; l.Target = loc; l.Strong = true; l.Why = "pasta de instalacao"; l.Size = DirSize(loc, ref bud);
                res.Add(l); seen.Add(loc);
            }
            // pastas/arquivos com o nome do programa
            List<string> roots = new List<string>();
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs"));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                ScanDir(root, nc, np, res, seen, 0);
            }
            // registro
            ScanReg(p, nc, np, loc, locOk, res, seen);
            // servicos
            try
            {
                using (RegistryKey sk = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                    if (sk != null)
                        foreach (string sn in sk.GetSubKeyNames())
                        {
                            try
                            {
                                using (RegistryKey k = sk.OpenSubKey(sn))
                                {
                                    string img = V(k, "ImagePath"), dn = V(k, "DisplayName");
                                    bool byPath = locOk && img.IndexOf(loc, StringComparison.OrdinalIgnoreCase) >= 0;
                                    bool st; bool byName = NameHit(sn, nc, np, out st) || NameHit(dn, nc, np, out st);
                                    if (!byPath && !byName) continue;
                                    ULeft l = new ULeft(); l.Kind = "Servico"; l.Target = sn; l.Extra = img.Length > 60 ? img.Substring(0, 60) + "..." : img; l.Strong = byPath;
                                    res.Add(l);
                                }
                            }
                            catch (Exception) { }
                        }
            }
            catch (Exception) { }
            // tarefas agendadas
            try
            {
                string td = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32\\Tasks");
                if (Directory.Exists(td))
                    foreach (string f in Directory.GetFiles(td, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            string rel = f.Substring(td.Length);
                            bool st; bool byName = NameHit(Path.GetFileName(f), nc, np, out st);
                            bool byPath = false;
                            if (!byName && locOk && new FileInfo(f).Length < 200000) byPath = File.ReadAllText(f).IndexOf(loc, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!byName && !byPath) continue;
                            if (rel.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase)) continue;
                            ULeft l = new ULeft(); l.Kind = "Tarefa"; l.Target = rel; l.Strong = byPath || st;
                            res.Add(l);
                        }
                        catch (Exception) { }
                    }
            }
            catch (Exception) { }
            return res;
        }
        static void ScanDir(string dir, string nc, string np, List<ULeft> res, HashSet<string> seen, int depth)
        {
            string[] ents;
            try { ents = Directory.GetFileSystemEntries(dir); } catch (Exception) { return; }
            foreach (string e in ents)
            {
                string name = Path.GetFileName(e);
                bool isDir = Directory.Exists(e);
                string baseName = isDir ? name : Path.GetFileNameWithoutExtension(name);
                bool strong;
                if (NameHit(baseName, nc, np, out strong) && !seen.Contains(e) && (isDir ? SafeFolder(e) : SafeFile(e)))
                {
                    ULeft l = new ULeft(); l.Kind = isDir ? "Pasta" : (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? "Atalho" : "Arquivo"); l.Target = e; l.Strong = strong;
                    if (isDir) { int bud = 30000; l.Size = DirSize(e, ref bud); } else { try { l.Size = new FileInfo(e).Length; } catch (Exception) { } }
                    res.Add(l); seen.Add(e);
                }
                else if (isDir && depth == 0 && np.Length >= 4 && Norm(name) == np)
                {
                    // pasta da editora (ex.: ...\Editora\Programa): olha um nivel dentro dela
                    ScanDir(e, nc, np, res, seen, 1);
                }
            }
        }
        static void ScanReg(UProg p, string nc, string np, string loc, bool locOk, List<ULeft> res, HashSet<string> seen)
        {
            if (p.KeyFull.Length > 0 && seen.Add(p.KeyFull))
            {
                ULeft u = new ULeft(); u.Kind = "Registro"; u.Target = p.KeyFull; u.Strong = true; u.Extra = "entrada em Programas e Recursos"; res.Add(u);
            }
            string[] sw = { @"SOFTWARE", @"SOFTWARE\WOW6432Node" };
            RegistryKey[] hv = { Registry.CurrentUser, Registry.LocalMachine };
            string[] hn = { "HKCU", "HKLM" };
            for (int h = 0; h < 2; h++)
                foreach (string b in sw)
                {
                    try
                    {
                        using (RegistryKey k = hv[h].OpenSubKey(b))
                        {
                            if (k == null) continue;
                            foreach (string sn in k.GetSubKeyNames())
                            {
                                bool st;
                                if (NameHit(sn, nc, np, out st))
                                {
                                    string full = hn[h] + "\\" + b + "\\" + sn;
                                    if (seen.Add(full)) { ULeft l = new ULeft(); l.Kind = "Registro"; l.Target = full; l.Strong = st; res.Add(l); }
                                }
                                else if (np.Length >= 4 && Norm(sn) == np)
                                {
                                    using (RegistryKey pk = k.OpenSubKey(sn))
                                        if (pk != null)
                                            foreach (string cn in pk.GetSubKeyNames())
                                            {
                                                bool st2;
                                                if (!NameHit(cn, nc, np, out st2)) continue;
                                                string full = hn[h] + "\\" + b + "\\" + sn + "\\" + cn;
                                                if (seen.Add(full)) { ULeft l = new ULeft(); l.Kind = "Registro"; l.Target = full; l.Strong = st2; res.Add(l); }
                                            }
                                }
                            }
                        }
                    }
                    catch (Exception) { }
                }
            // programas que iniciam com o Windows
            string[] runs = { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run" };
            for (int h = 0; h < 2; h++)
                foreach (string rp in runs)
                {
                    try
                    {
                        using (RegistryKey k = hv[h].OpenSubKey(rp))
                        {
                            if (k == null) continue;
                            foreach (string vn in k.GetValueNames())
                            {
                                string data = V(k, vn);
                                bool byPath = locOk && data.IndexOf(loc, StringComparison.OrdinalIgnoreCase) >= 0;
                                bool st; bool byName = NameHit(vn, nc, np, out st);
                                if (!byPath && !byName) continue;
                                ULeft l = new ULeft(); l.Kind = "Inicializacao"; l.Target = hn[h] + "\\" + rp; l.Extra = vn; l.Strong = byPath || st; res.Add(l);
                            }
                        }
                    }
                    catch (Exception) { }
                }
        }

        // ----- remocao -----
        void DoRemove()
        {
            List<ULeft> items = new List<ULeft>();
            foreach (ListViewItem it in lvL.CheckedItems) items.Add((ULeft)it.Tag);
            if (items.Count == 0) { Msg("Nenhum item marcado."); return; }
            bool bin = chkBin.Checked, bak = chkReg.Checked;
            if (Dlg.Show("Remover " + items.Count + " item(ns) marcado(s)?\n\n" + (bin ? "Pastas e arquivos irao para a Lixeira.\n" : "Pastas e arquivos serao APAGADOS de vez.\n") + (bak ? "Uma copia das chaves do registro sera salva em BunoTunner_backup." : "Sem copia do registro."), "BunoTunner - Remover", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            Busy(true, "Removendo...");
            UProg kp = lastProg;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0; rebootN = 0; List<string> fails = new List<string>();
                if (kp != null) KillProcs(kp);
                string bdir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BunoTunner_backup\\" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                int regN = 0;
                foreach (ULeft l in items)
                {
                    try
                    {
                        string err = RemoveOne(l, bin, bak, bdir, ref regN);
                        if (err == null) ok++; else fails.Add(l.Kind + " " + l.Target + " -> " + err);
                    }
                    catch (Exception ex) { fails.Add(l.Kind + " " + l.Target + " -> " + ex.Message); }
                }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        Busy(false, ok + " item(ns) removido(s)" + (fails.Count > 0 ? ", " + fails.Count + " com falha." : ".") + (rebootN > 0 ? " " + rebootN + " arquivo(s) em uso serao apagados no proximo reinicio do Windows." : "") + (bak && regN > 0 ? " Copias do registro em BunoTunner_backup." : ""));
                        if (fails.Count > 0)
                            Dlg.Show("Nao foi possivel remover:\n\n" + string.Join("\n", fails.GetRange(0, Math.Min(12, fails.Count)).ToArray()) + "\n\nSe o arquivo esta em uso, feche o programa (ou use 'Remocao forcada') e tente de novo; ou reinicie o PC.", "BunoTunner", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        LoadProgs();
                        lvL.Items.Clear();
                    }));
                }
                catch (Exception) { }
            });
        }
        static string RemoveOne(ULeft l, bool bin, bool bak, string bdir, ref int regN)
        {
            switch (l.Kind)
            {
                case "Pasta":
                    if (!Directory.Exists(l.Target)) return null;
                    if (!SafeFolder(l.Target)) return "pasta protegida";
                    try
                    {
                        if (bin) Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(l.Target, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        else Directory.Delete(l.Target, true);
                        if (!Directory.Exists(l.Target)) return null;
                    }
                    catch (Exception) { }
                    return ForceDelete(l.Target, true);
                case "Arquivo":
                case "Atalho":
                    if (!File.Exists(l.Target)) return null;
                    if (!SafeFile(l.Target)) return "arquivo protegido";
                    try
                    {
                        if (bin) Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(l.Target, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        else File.Delete(l.Target);
                        if (!File.Exists(l.Target)) return null;
                    }
                    catch (Exception) { }
                    return ForceDelete(l.Target, false);
                case "Registro":
                    {
                        if (bak) { Directory.CreateDirectory(bdir); int c; Proc.Run("reg.exe", "export \"" + l.Target + "\" \"" + Path.Combine(bdir, "chave_" + (++regN) + ".reg") + "\" /y", 20000, out c); }
                        string sub; RegistryKey root = RegRoot(l.Target, out sub);
                        if (root == null || sub.Length < 12 || sub.IndexOf('\\') < 0) return "chave protegida";
                        try { root.DeleteSubKeyTree(sub, false); return null; }
                        catch (Exception)
                        {
                            int c; string o = Proc.Run("reg.exe", "delete \"" + l.Target + "\" /f", 15000, out c);
                            return c == 0 ? null : "sem permissao no registro: " + o;
                        }
                    }
                case "Inicializacao":
                    {
                        string sub; RegistryKey root = RegRoot(l.Target, out sub);
                        if (root == null) return "chave invalida";
                        if (bak) { Directory.CreateDirectory(bdir); File.AppendAllText(Path.Combine(bdir, "inicializacao.txt"), l.Target + "  " + l.Extra + " = " + ReadVal(root, sub, l.Extra) + "\r\n"); }
                        using (RegistryKey k = root.OpenSubKey(sub, true)) if (k != null) k.DeleteValue(l.Extra, false);
                        return null;
                    }
                case "Servico":
                    {
                        int c;
                        Proc.Run("sc.exe", "stop \"" + l.Target + "\"", 15000, out c);
                        string o = Proc.Run("sc.exe", "delete \"" + l.Target + "\"", 15000, out c);
                        return c == 0 ? null : o;
                    }
                case "Tarefa":
                    {
                        int c;
                        string o = Proc.Run("schtasks.exe", "/delete /tn \"" + l.Target + "\" /f", 15000, out c);
                        return c == 0 ? null : o;
                    }
            }
            return "tipo desconhecido";
        }
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool MoveFileEx(string from, string to, int flags);

        // apagar a forca: toma posse, da controle total ao Administradores, tira somente-leitura e tenta de novo;
        // o que continuar em uso e agendado para apagar no proximo reinicio do Windows
        static string ForceDelete(string path, bool dir)
        {
            int c;
            Proc.Run("takeown.exe", "/f \"" + path + "\"" + (dir ? " /r" : ""), 60000, out c);
            Proc.Run("icacls.exe", "\"" + path + "\" /grant *S-1-5-32-544:F" + (dir ? " /t" : "") + " /c /q", 60000, out c);
            try
            {
                if (dir)
                {
                    foreach (string f in Directory.GetFiles(path, "*", SearchOption.AllDirectories)) { try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch (Exception) { } }
                    try { Directory.Delete(path, true); } catch (Exception) { }
                }
                else { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
            }
            catch (Exception) { }
            if (dir ? !Directory.Exists(path) : !File.Exists(path)) return null;
            // ainda em uso: agenda para o reinicio (arquivos primeiro, pastas da mais funda para a mais rasa)
            try
            {
                List<string> files = new List<string>(), dirs = new List<string>();
                if (dir) { files.AddRange(Directory.GetFiles(path, "*", SearchOption.AllDirectories)); dirs.AddRange(Directory.GetDirectories(path, "*", SearchOption.AllDirectories)); dirs.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); }); dirs.Add(path); }
                else files.Add(path);
                bool ok = true;
                foreach (string f in files) { if (MoveFileEx(f, null, 4)) rebootN++; else ok = false; }
                foreach (string d in dirs) if (!MoveFileEx(d, null, 4)) ok = false;
                return ok ? null : "em uso e nao foi possivel agendar a exclusao";
            }
            catch (Exception ex) { return ex.Message; }
        }

        static string ReadVal(RegistryKey root, string sub, string name)
        {
            try { using (RegistryKey k = root.OpenSubKey(sub)) return k == null ? "" : V(k, name); } catch (Exception) { return ""; }
        }
        static RegistryKey RegRoot(string full, out string sub)
        {
            sub = "";
            int i = full.IndexOf('\\'); if (i < 0) return null;
            string h = full.Substring(0, i); sub = full.Substring(i + 1);
            if (h == "HKLM") return Registry.LocalMachine;
            if (h == "HKCU") return Registry.CurrentUser;
            return null;
        }
    }

    // ------------------------------------------------------------------
    //  Internet: teste de velocidade real (ping, download, upload) + informacoes da rede (so ao clicar)
    // ------------------------------------------------------------------
    class SpeedGauge : Control
    {
        public double Value;        // alvo (Mbps)
        public string Caption = "", Unit = "Mbps";
        double shown, max = 100;
        static readonly double[] Scales = { 10, 25, 50, 100, 250, 500, 1000, 2500, 5000 };
        Timer tm = new Timer();
        public SpeedGauge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            tm.Interval = 40;
            tm.Tick += delegate
            {
                if (!Visible) return;
                for (int i = 0; i < Scales.Length - 1 && Value * 1.08 > max && max < Scales[Scales.Length - 1]; i++)
                    foreach (double sc in Scales) if (sc > max) { max = sc; break; }
                double d = Value - shown;
                if (Math.Abs(d) > 0.01) { shown += d * 0.22; Invalidate(); }
            };
            tm.Start();
        }
        public void Reset() { Value = 0; shown = 0; max = 100; Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing) tm.Dispose(); base.Dispose(disposing); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent != null ? Parent.BackColor : Xp.Face);
            float size = Math.Min(Width, Height * 1.15f) - 24;
            if (size < 60) return;
            float cx = Width / 2f, cy = Height * 0.56f, rad = size / 2f;
            RectangleF box = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);
            float start = 135f, sweep = 270f;
            using (Pen track = new Pen(Color.FromArgb(70, Xp.Dim), rad * 0.13f)) { track.StartCap = LineCap.Round; track.EndCap = LineCap.Round; g.DrawArc(track, box, start, sweep); }
            double frac = Math.Max(0, Math.Min(1, shown / max));
            Color col = frac < 0.55 ? Color.FromArgb(40, 190, 80) : frac < 0.8 ? Color.FromArgb(235, 190, 30) : Color.FromArgb(235, 90, 40);
            if (frac > 0.002)
                using (Pen vp = new Pen(col, rad * 0.13f)) { vp.StartCap = LineCap.Round; vp.EndCap = LineCap.Round; g.DrawArc(vp, box, start, (float)(sweep * frac)); }
            using (Pen tk = new Pen(Xp.Text, 1.5f))
            using (Font f = new Font(Xp.Ui.FontFamily, 8f))
                for (int i = 0; i <= 10; i++)
                {
                    double a = (start + sweep * i / 10.0) * Math.PI / 180;
                    float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                    g.DrawLine(tk, cx + ca * rad * 0.80f, cy + sa * rad * 0.80f, cx + ca * rad * 0.70f, cy + sa * rad * 0.70f);
                    if (i % 2 == 0)
                    {
                        string t = (max * i / 10.0).ToString("0");
                        Size ts = TextRenderer.MeasureText(t, f);
                        TextRenderer.DrawText(g, t, f, new Point((int)(cx + ca * rad * 0.58f - ts.Width / 2f), (int)(cy + sa * rad * 0.58f - ts.Height / 2f)), Xp.Dim);
                    }
                }
            double na = (start + sweep * frac) * Math.PI / 180;
            using (Pen np = new Pen(Color.FromArgb(220, 40, 40), 3f)) { np.EndCap = LineCap.Round; g.DrawLine(np, cx, cy, cx + (float)Math.Cos(na) * rad * 0.66f, cy + (float)Math.Sin(na) * rad * 0.66f); }
            g.FillEllipse(Brushes.DimGray, cx - 7, cy - 7, 14, 14);
            using (Font big = new Font(Xp.Ui.FontFamily, Math.Max(16f, rad * 0.26f), FontStyle.Bold))
            {
                string v = shown >= 100 ? shown.ToString("0") : shown.ToString("0.0");
                Size vs = TextRenderer.MeasureText(v, big);
                TextRenderer.DrawText(g, v, big, new Point((int)(cx - vs.Width / 2f), (int)(cy + rad * 0.22f)), Xp.Text);
                Size us = TextRenderer.MeasureText(Unit, Xp.UiBold);
                TextRenderer.DrawText(g, Unit, Xp.UiBold, new Point((int)(cx - us.Width / 2f), (int)(cy + rad * 0.22f + vs.Height - 2)), Xp.Dim);
            }
            string capT = Lang.T(Caption);
            Size cs = TextRenderer.MeasureText(capT, Xp.UiBold);
            TextRenderer.DrawText(g, capT, Xp.UiBold, new Point((int)(cx - cs.Width / 2f), (int)(cy - rad * 1.0f)), Xp.GroupText);
        }
    }

    class NetPage : Panel
    {
        SpeedGauge gauge = new SpeedGauge();
        Label lblPing, lblJit, lblDown, lblUp, lblMsg, lblHint;
        XpButton bStart, bInfo, bCopy, bHide;
        TextBox tbInfo = new TextBox();
        volatile bool running, cancel;
        long bytes;
        Timer tm = new Timer();

        public NetPage()
        {
            Dock = DockStyle.Fill;
            XpGroup g1 = new XpGroup(); g1.Title = "Teste de velocidade da internet"; g1.SetBounds(0, 0, 676, 318);
            gauge.SetBounds(10, 24, 420, 280); gauge.Caption = "Pronto"; g1.Controls.Add(gauge);
            UI.Lbl(g1, "Ping", 450, 30, Xp.UiBold); lblPing = UI.Lbl(g1, "-", 450, 46, Xp.Big, Xp.GroupText);
            UI.Lbl(g1, "Jitter", 560, 30, Xp.UiBold); lblJit = UI.Lbl(g1, "-", 560, 46, Xp.Big, Xp.GroupText);
            UI.Lbl(g1, "Download", 450, 100, Xp.UiBold); lblDown = UI.Lbl(g1, "-", 450, 116, Xp.Big, Xp.GroupText);
            UI.Lbl(g1, "Upload", 450, 170, Xp.UiBold); lblUp = UI.Lbl(g1, "-", 450, 186, Xp.Big, Xp.GroupText);
            bStart = UI.Btn(g1, "Iniciar teste", 450, 236, 210, delegate { Toggle(); });
            bStart.Height = 32;
            lblMsg = UI.Lbl(g1, "Mede ping, download e upload de verdade (servidor da Cloudflare).", 450, 274, Xp.Ui, Xp.Dim);
            lblMsg.AutoSize = false; lblMsg.SetBounds(450, 272, 214, 40);
            Controls.Add(g1);

            XpGroup g2 = new XpGroup(); g2.Title = "Informacoes da rede"; g2.SetBounds(0, 326, 676, 258);
            bInfo = UI.Btn(g2, "Mostrar informacoes da rede", 12, 26, 220, delegate { ShowInfo(); });
            bInfo.Height = 28;
            bCopy = UI.Btn(g2, "Copiar", 238, 26, 80, delegate { try { if (tbInfo.Text.Length > 0) Clipboard.SetText(tbInfo.Text); } catch (Exception) { } });
            bHide = UI.Btn(g2, "Ocultar", 324, 26, 80, delegate { tbInfo.Text = ""; tbInfo.Visible = false; lblHint.Visible = true; });
            bCopy.Height = bHide.Height = 28;
            lblHint = UI.Lbl(g2, "Por privacidade, nada e mostrado ate voce clicar. Ao clicar aparecem dados pessoais (IPs, MAC, provedor, cidade, nome do Wi-Fi).", 12, 70, Xp.Ui, Xp.Dim);
            lblHint.AutoSize = false; lblHint.SetBounds(12, 66, 650, 40);
            tbInfo.Multiline = true; tbInfo.ReadOnly = true; tbInfo.ScrollBars = ScrollBars.Vertical; tbInfo.Font = new Font("Consolas", 9f);
            tbInfo.BackColor = Xp.EditBg; tbInfo.ForeColor = Xp.Text; tbInfo.SetBounds(12, 62, 652, 186); tbInfo.Visible = false;
            g2.Controls.Add(tbInfo);
            Controls.Add(g2);
            tm.Interval = 200; tm.Tick += delegate { Sample(); };
        }

        // ---------- teste ----------
        void Toggle()
        {
            if (running) { cancel = true; lblMsg.Text = "Cancelando..."; return; }
            running = true; cancel = false;
            bStart.Text = "Cancelar";
            lblPing.Text = lblJit.Text = lblDown.Text = lblUp.Text = "-";
            gauge.Reset();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { Run(); });
        }
        void UI2(Action a) { try { BeginInvoke(a); } catch (Exception) { } }

        void Run()
        {
            string err = null;
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;   // TLS 1.2
                ServicePointManager.DefaultConnectionLimit = 32;
                ServicePointManager.Expect100Continue = false;
                // 1) ping
                UI2(delegate { gauge.Caption = "Ping..."; gauge.Unit = "ms"; gauge.Reset(); lblMsg.Text = "Medindo latencia..."; });
                List<double> rt = new List<double>();
                for (int i = 0; i < 8 && !cancel; i++)
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("https://speed.cloudflare.com/__down?bytes=0");
                    rq.Timeout = 6000; rq.KeepAlive = true;
                    using (WebResponse rs = rq.GetResponse()) { }
                    sw.Stop();
                    if (i > 0) { rt.Add(sw.Elapsed.TotalMilliseconds); double cur = sw.Elapsed.TotalMilliseconds; UI2(delegate { gauge.Value = cur; }); }
                }
                if (rt.Count > 0)
                {
                    double mn = double.MaxValue, jit = 0; foreach (double d in rt) mn = Math.Min(mn, d);
                    for (int i = 1; i < rt.Count; i++) jit += Math.Abs(rt[i] - rt[i - 1]);
                    jit = rt.Count > 1 ? jit / (rt.Count - 1) : 0;
                    UI2(delegate { lblPing.Text = mn.ToString("0") + " ms"; lblJit.Text = jit.ToString("0.0") + " ms"; });
                }
                // 2) download   3) upload
                if (!cancel) { double d = Phase(false); UI2(delegate { lblDown.Text = d.ToString("0.0") + " Mbps"; }); }
                if (!cancel) { double u = Phase(true); UI2(delegate { lblUp.Text = u.ToString("0.0") + " Mbps"; }); }
            }
            catch (Exception ex) { err = ex.Message; }
            UI2(delegate
            {
                tm.Stop(); running = false; bStart.Text = "Iniciar teste";
                gauge.Caption = err != null ? "Falhou" : (cancel ? "Cancelado" : "Concluido"); gauge.Unit = "Mbps";
                lblMsg.Text = err != null ? "Nao foi possivel medir (sem internet ou bloqueio de rede?): " + err : (cancel ? "Teste cancelado." : "Teste concluido.");
            });
        }

        long lastB; DateTime lastT; double ema;
        void Sample()
        {
            long b = System.Threading.Interlocked.Read(ref bytes); DateTime now = DateTime.UtcNow;
            double dt = (now - lastT).TotalSeconds;
            if (dt > 0.05)
            {
                double mbps = (b - lastB) * 8.0 / 1e6 / dt;
                ema = ema <= 0 ? mbps : ema * 0.6 + mbps * 0.4;
                gauge.Value = ema; lastB = b; lastT = now;
            }
        }

        // devolve a media (Mbps) depois de 1,5 s de aquecimento
        double Phase(bool up)
        {
            const int Secs = 10, Warm = 1500, N = 4;
            System.Threading.Interlocked.Exchange(ref bytes, 0); lastB = 0; ema = 0; lastT = DateTime.UtcNow;
            UI2(delegate { gauge.Reset(); gauge.Caption = up ? "Upload" : "Download"; gauge.Unit = "Mbps"; lblMsg.Text = up ? "Medindo upload..." : "Medindo download..."; tm.Start(); });
            DateTime end = DateTime.UtcNow.AddSeconds(Secs);
            List<HttpWebRequest> reqs = new List<HttpWebRequest>(); object rl = new object();
            List<System.Threading.Thread> ths = new List<System.Threading.Thread>();
            for (int i = 0; i < N; i++)
            {
                System.Threading.Thread t = new System.Threading.Thread(delegate ()
                {
                    byte[] buf = new byte[65536]; new Random().NextBytes(buf);
                    while (!cancel && DateTime.UtcNow < end)
                    {
                        try
                        {
                            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(up ? "https://speed.cloudflare.com/__up" : "https://speed.cloudflare.com/__down?bytes=50000000");
                            rq.Timeout = 15000; rq.ReadWriteTimeout = 8000; rq.Proxy = WebRequest.DefaultWebProxy;
                            lock (rl) reqs.Add(rq);
                            if (up)
                            {
                                rq.Method = "POST"; rq.ContentType = "application/octet-stream"; rq.AllowWriteStreamBuffering = false; rq.ContentLength = 25000000;
                                using (Stream s = rq.GetRequestStream())
                                {
                                    long sent = 0;
                                    while (sent < 25000000 && !cancel && DateTime.UtcNow < end) { s.Write(buf, 0, buf.Length); sent += buf.Length; System.Threading.Interlocked.Add(ref bytes, buf.Length); }
                                }
                            }
                            else
                                using (WebResponse rs = rq.GetResponse())
                                using (Stream s = rs.GetResponseStream())
                                {
                                    int n;
                                    while (!cancel && DateTime.UtcNow < end && (n = s.Read(buf, 0, buf.Length)) > 0) System.Threading.Interlocked.Add(ref bytes, n);
                                }
                            rq.Abort();
                        }
                        catch (Exception) { if (cancel || DateTime.UtcNow >= end) break; System.Threading.Thread.Sleep(200); }
                    }
                });
                t.IsBackground = true; t.Start(); ths.Add(t);
            }
            long b0 = 0; DateTime t0 = DateTime.UtcNow.AddMilliseconds(Warm); bool marked = false;
            while (!cancel && DateTime.UtcNow < end)
            {
                if (!marked && DateTime.UtcNow >= t0) { b0 = System.Threading.Interlocked.Read(ref bytes); t0 = DateTime.UtcNow; marked = true; }
                System.Threading.Thread.Sleep(50);
            }
            long b1 = System.Threading.Interlocked.Read(ref bytes); DateTime t1 = DateTime.UtcNow;
            lock (rl) foreach (HttpWebRequest r in reqs) { try { r.Abort(); } catch (Exception) { } }
            foreach (System.Threading.Thread t in ths) t.Join(1500);
            UI2(delegate { tm.Stop(); });
            double secs = (t1 - t0).TotalSeconds;
            return marked && secs > 0.5 ? (b1 - b0) * 8.0 / 1e6 / secs : 0;
        }

        // ---------- informacoes (so ao clicar) ----------
        void ShowInfo()
        {
            if (Dlg.Show("ATENCAO: as informacoes a seguir sao PESSOAIS.\n\nVao aparecer seu IP interno e externo, enderecos MAC, provedor, cidade aproximada, nome da rede Wi-Fi e DNS.\nNao compartilhe prints desta tela.\n\nMostrar mesmo assim?", "BunoTunner - Dados pessoais", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            bInfo.Enabled = false; lblHint.Text = "Coletando informacoes..."; lblHint.Visible = true; tbInfo.Visible = false;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string txt;
                try { txt = Gather(); } catch (Exception ex) { txt = "Erro: " + ex.Message; }
                UI2(delegate { tbInfo.Text = txt; tbInfo.Visible = true; lblHint.Visible = false; bInfo.Enabled = true; tbInfo.SelectionStart = 0; });
            });
        }

        static string J(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }
        static string Get(string url)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
            rq.Timeout = 8000; rq.UserAgent = "BunoTunner";
            using (WebResponse rs = rq.GetResponse())
            using (StreamReader sr = new StreamReader(rs.GetResponseStream())) return sr.ReadToEnd();
        }
        static string Gather()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Este computador ===");
            sb.AppendLine("Nome do PC: " + Environment.MachineName);
            sb.AppendLine("Usuario: " + Environment.UserDomainName + "\\" + Environment.UserName);
            foreach (System.Net.NetworkInformation.NetworkInterface ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                System.Net.NetworkInformation.IPInterfaceProperties ip = ni.GetIPProperties();
                sb.AppendLine();
                sb.AppendLine("--- Adaptador: " + ni.Name + " ---");
                sb.AppendLine("Descricao: " + ni.Description);
                sb.AppendLine("Tipo: " + ni.NetworkInterfaceType + "   Velocidade do link: " + (ni.Speed / 1000000) + " Mbps");
                string mac = ni.GetPhysicalAddress().ToString();
                if (mac.Length == 12) mac = Regex.Replace(mac, "(.{2})(?!$)", "$1-");
                sb.AppendLine("MAC: " + mac);
                foreach (System.Net.NetworkInformation.UnicastIPAddressInformation u in ip.UnicastAddresses)
                    sb.AppendLine((u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "IPv4: " : "IPv6: ") + u.Address + (u.IPv4Mask != null && u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "   mascara " + u.IPv4Mask : ""));
                foreach (System.Net.NetworkInformation.GatewayIPAddressInformation gw in ip.GatewayAddresses) sb.AppendLine("Gateway: " + gw.Address);
                foreach (System.Net.IPAddress d in ip.DnsAddresses) sb.AppendLine("DNS: " + d);
                foreach (System.Net.IPAddress d in ip.DhcpServerAddresses) sb.AppendLine("Servidor DHCP: " + d);
                if (!string.IsNullOrEmpty(ip.DnsSuffix)) sb.AppendLine("Sufixo DNS: " + ip.DnsSuffix);
            }
            int c;
            string wl = Proc.Run("netsh.exe", "wlan show interfaces", 6000, out c);
            if (c == 0 && wl.Length > 20) { sb.AppendLine(); sb.AppendLine("=== Wi-Fi ==="); sb.AppendLine(wl); }
            sb.AppendLine();
            sb.AppendLine("=== Internet (visto de fora) ===");
            try
            {
                string j = Get("https://ipinfo.io/json");
                sb.AppendLine("IP externo: " + J(j, "ip"));
                string hn = J(j, "hostname"); if (hn.Length > 0) sb.AppendLine("Host reverso: " + hn);
                sb.AppendLine("Provedor (ISP/ASN): " + J(j, "org"));
                sb.AppendLine("Cidade: " + J(j, "city") + " - " + J(j, "region") + " - " + J(j, "country") + "  CEP: " + J(j, "postal"));
                sb.AppendLine("Localizacao aproximada: " + J(j, "loc"));
                sb.AppendLine("Fuso horario: " + J(j, "timezone"));
            }
            catch (Exception ex) { sb.AppendLine("Nao foi possivel consultar o IP externo: " + ex.Message); }
            try { string v6 = Get("https://api64.ipify.org").Trim(); sb.AppendLine("IP preferido (v6 se houver): " + v6); } catch (Exception) { }
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------
    //  Saude de armazenamento: vida util restante, temperatura e S.M.A.R.T. (NVMe e SATA)
    // ------------------------------------------------------------------
    class DiskInfo
    {
        public string Id = "", Name = "", Media = "", Bus = "", Health = "", Fw = "";
        public long Size;
        public double Temp = double.NaN, TempMax = double.NaN, Wear = double.NaN, Hours = double.NaN, ReadErr = double.NaN, WriteErr = double.NaN, Cycles = double.NaN;
        public bool IsSsd { get { return Media == "SSD" || Bus == "NVMe"; } }
        public double Life { get { return double.IsNaN(Wear) ? double.NaN : Math.Max(0, 100 - Wear); } }
        public string Kind { get { return (Bus.Length > 0 ? Bus + " " : "") + (Media == "SSD" ? "SSD" : Media == "HDD" ? "HDD" : Media); } }
    }

    class StoragePage : Panel
    {
        ListView lvD = new ListView(), lvA = new ListView();
        Label lblHealth, lblLife, lblTemp, lblAdvice, lblMsg;
        XpProgress pbLife, pbTemp;
        List<DiskInfo> disks = new List<DiskInfo>();
        DiskInfo cur; bool loading, loadedOnce;
        Timer tm = new Timer();

        public StoragePage()
        {
            Dock = DockStyle.Fill;
            XpGroup g1 = new XpGroup(); g1.Title = "Seus discos"; g1.SetBounds(0, 0, 676, 178);
            lvD.SetBounds(12, 24, 652, 112); lvD.View = View.Details; lvD.FullRowSelect = true; lvD.GridLines = true; lvD.HideSelection = false; lvD.MultiSelect = false;
            lvD.BackColor = Xp.EditBg; lvD.Font = Xp.Ui;
            lvD.Columns.Add("Disco", 210); lvD.Columns.Add("Tipo", 90); lvD.Columns.Add("Tamanho", 70); lvD.Columns.Add("Saude", 70); lvD.Columns.Add("Vida", 60); lvD.Columns.Add("Temp.", 60); lvD.Columns.Add("Horas", 60);
            UI.DoubleBuffer(lvD);
            lvD.SelectedIndexChanged += delegate { cur = lvD.SelectedItems.Count > 0 ? lvD.SelectedItems[0].Tag as DiskInfo : null; ShowDetail(); };
            g1.Controls.Add(lvD);
            UI.Btn(g1, "Atualizar", 12, 144, 90, delegate { Load(); });
            lblMsg = UI.Lbl(g1, "", 112, 148, Xp.Ui, Xp.Dim);
            lblMsg.AutoSize = false; lblMsg.SetBounds(112, 146, 550, 26);
            Controls.Add(g1);

            XpGroup g2 = new XpGroup(); g2.Title = "Detalhes do disco selecionado"; g2.SetBounds(0, 186, 676, 398);
            UI.Lbl(g2, "Saude geral:", 14, 28, Xp.UiBold);
            lblHealth = UI.Lbl(g2, "-", 130, 24, Xp.Big, Xp.GroupText);
            UI.Lbl(g2, "Vida util restante:", 14, 62, Xp.UiBold);
            pbLife = new XpProgress(); pbLife.SetBounds(150, 60, 330, 18); g2.Controls.Add(pbLife);
            lblLife = UI.Lbl(g2, "-", 490, 62, Xp.UiBold, Xp.GroupText);
            UI.Lbl(g2, "Temperatura:", 14, 92, Xp.UiBold);
            pbTemp = new XpProgress(); pbTemp.SetBounds(150, 90, 330, 18); g2.Controls.Add(pbTemp);
            lblTemp = UI.Lbl(g2, "-", 490, 92, Xp.UiBold, Xp.GroupText);
            lblAdvice = UI.Lbl(g2, "Selecione um disco na lista acima.", 14, 120, Xp.Ui, Xp.Dim);
            lblAdvice.AutoSize = false; lblAdvice.SetBounds(14, 120, 648, 44);
            lvA.SetBounds(12, 168, 652, 220); lvA.View = View.Details; lvA.FullRowSelect = true; lvA.GridLines = true;
            lvA.BackColor = Xp.EditBg; lvA.Font = Xp.Ui;
            lvA.Columns.Add("Atributo / sensor", 330); lvA.Columns.Add("Valor", 300);
            UI.DoubleBuffer(lvA);
            g2.Controls.Add(lvA);
            Controls.Add(g2);

            VisibleChanged += delegate { if (Visible && !loadedOnce) { loadedOnce = true; Load(); } tm.Enabled = Visible; };
            tm.Interval = 3000; tm.Tick += delegate { LiveTemp(); };
        }

        static double D(string s) { double v; return double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN; }

        void Load()
        {
            if (loading) return;
            loading = true; lblMsg.Text = "Lendo os discos (Windows Storage + S.M.A.R.T.)...";
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<DiskInfo> l = new List<DiskInfo>(); string err = null;
                try
                {
                    int c;
                    string cmd = "-NoProfile -NonInteractive -Command \"Get-PhysicalDisk | ForEach-Object { $r = $_ | Get-StorageReliabilityCounter -ErrorAction SilentlyContinue; ('DISK',$_.DeviceId,$_.FriendlyName,$_.MediaType,$_.BusType,$_.HealthStatus,$_.Size,$_.FirmwareVersion,$r.Temperature,$r.TemperatureMax,$r.Wear,$r.PowerOnHours,$r.ReadErrorsTotal,$r.WriteErrorsTotal,$r.StartStopCycleCount) -join '|' }\"";
                    string o = Proc.Run("powershell.exe", cmd, 45000, out c);
                    foreach (string line in o.Replace("\r", "").Split('\n'))
                    {
                        if (!line.StartsWith("DISK|")) continue;
                        string[] p = line.Split('|');
                        if (p.Length < 15) continue;
                        DiskInfo d = new DiskInfo();
                        d.Id = p[1]; d.Name = p[2].Trim(); d.Media = p[3].Trim(); d.Bus = p[4].Trim(); d.Health = p[5].Trim(); d.Fw = p[7].Trim();
                        long sz; long.TryParse(p[6], out sz); d.Size = sz;
                        d.Temp = D(p[8]); d.TempMax = D(p[9]); d.Wear = D(p[10]); d.Hours = D(p[11]); d.ReadErr = D(p[12]); d.WriteErr = D(p[13]); d.Cycles = D(p[14]);
                        if (d.Temp <= 0) d.Temp = double.NaN;
                        l.Add(d);
                    }
                    if (l.Count == 0) err = o.Length > 0 && !o.StartsWith("DISK") ? o : "o Windows nao informou discos";
                }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        loading = false; disks = l; FillList();
                        lblMsg.Text = err != null ? "Nao foi possivel ler: " + err : l.Count + " disco(s). Alguns discos USB/RAID nao informam S.M.A.R.T. ao Windows.";
                    }));
                }
                catch (Exception) { }
            });
        }

        static string HealthText(DiskInfo d)
        {
            double life = d.Life;
            if (d.Health.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase) || (!double.IsNaN(life) && life < 10)) return "Ruim";
            if (d.Health.Equals("Warning", StringComparison.OrdinalIgnoreCase) || (!double.IsNaN(life) && life < 30) || (!double.IsNaN(d.Temp) && d.Temp >= 70) || (!double.IsNaN(d.ReadErr) && d.ReadErr > 0) || (!double.IsNaN(d.WriteErr) && d.WriteErr > 0)) return "Atencao";
            if (d.Health.Length == 0 || d.Health.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return "Sem dados";
            return "Boa";
        }
        Color HealthColor(string h) { return h == "Boa" ? Xp.Good : h == "Sem dados" ? Xp.Dim : Xp.Bad; }

        string Fmt(double v, string unit) { return double.IsNaN(v) ? "-" : v.ToString("0.#") + unit; }

        void FillList()
        {
            string keep = cur != null ? cur.Id : null;
            lvD.Items.Clear();
            foreach (DiskInfo d in disks)
            {
                string h = HealthText(d);
                ListViewItem it = new ListViewItem(d.Name);
                it.SubItems.Add(d.Kind); it.SubItems.Add((d.Size / 1073741824L) + " GB"); it.SubItems.Add(h);
                it.SubItems.Add(double.IsNaN(d.Life) ? "-" : d.Life.ToString("0") + "%"); it.SubItems.Add(Fmt(d.Temp, " C")); it.SubItems.Add(double.IsNaN(d.Hours) ? "-" : d.Hours.ToString("0"));
                it.Tag = d; it.SubItems[3].ForeColor = HealthColor(h);
                lvD.Items.Add(it);
                if (keep != null && d.Id == keep) it.Selected = true;
            }
            if (lvD.SelectedItems.Count == 0 && lvD.Items.Count > 0) lvD.Items[0].Selected = true;
        }

        // sensores do LibreHardwareMonitor para o disco (temperatura ao vivo, vida, dados gravados...)
        static string Norm(string s) { return Regex.Replace((s ?? "").ToLower(), "[^a-z0-9]", ""); }
        List<Sn> Sensors(DiskInfo d)
        {
            List<Sn> r = new List<Sn>(); string n = Norm(d.Name);
            if (n.Length == 0) return r;
            foreach (Sn s in SensorHub.PcSensors)
                if (s.Group.StartsWith("Armazenamento") && (Norm(s.Group).Contains(n) || n.Contains(Norm(s.Group.Substring(Math.Min(s.Group.Length, 24)))) )) r.Add(s);
            return r;
        }

        void ShowDetail()
        {
            lvA.Items.Clear();
            if (cur == null) { lblHealth.Text = "-"; lblLife.Text = "-"; lblTemp.Text = "-"; pbLife.Value = 0; pbTemp.Value = 0; return; }
            DiskInfo d = cur; string h = HealthText(d);
            lblHealth.Text = h; lblHealth.ForeColor = HealthColor(h);
            if (double.IsNaN(d.Life)) { pbLife.Value = 0; lblLife.Text = d.IsSsd ? "indisponivel" : "n/a (HDD)"; } else { pbLife.Value = (int)d.Life; lblLife.Text = d.Life.ToString("0") + "% restante"; }
            LiveTemp();
            List<string[]> rows = new List<string[]>();
            rows.Add(new string[] { "Modelo", d.Name }); rows.Add(new string[] { "Tipo / interface", d.Kind });
            rows.Add(new string[] { "Capacidade", (d.Size / 1000000000L) + " GB (" + (d.Size / 1073741824L) + " GiB)" });
            rows.Add(new string[] { "Firmware", d.Fw }); rows.Add(new string[] { "Estado informado pelo Windows", d.Health });
            if (!double.IsNaN(d.Wear)) rows.Add(new string[] { "Desgaste (uso da vida util)", d.Wear.ToString("0") + "%" });
            if (!double.IsNaN(d.Hours)) rows.Add(new string[] { "Horas ligado", d.Hours.ToString("0") + " h (~" + (d.Hours / 24).ToString("0") + " dias)" });
            if (!double.IsNaN(d.Cycles)) rows.Add(new string[] { "Ciclos liga/desliga", d.Cycles.ToString("0") });
            if (!double.IsNaN(d.TempMax)) rows.Add(new string[] { "Temperatura maxima registrada", d.TempMax.ToString("0") + " C" });
            rows.Add(new string[] { "Erros de leitura (total)", Fmt(d.ReadErr, "") }); rows.Add(new string[] { "Erros de escrita (total)", Fmt(d.WriteErr, "") });
            foreach (Sn s in Sensors(d))
            {
                float? v = s.Last; if (!v.HasValue) continue;
                string unit = s.Type == "Temperature" ? " C" : s.Type == "Load" || s.Type == "Level" ? " %" : s.Type == "Data" ? " GB" : s.Type == "Throughput" ? " B/s" : "";
                rows.Add(new string[] { "S.M.A.R.T. - " + s.Name, v.Value.ToString("0.##") + unit });
            }
            foreach (string[] r in rows) lvA.Items.Add(new ListViewItem(r));
            string adv;
            if (h == "Ruim") adv = "ATENCAO: o disco indica falha iminente ou pouca vida util. Faca backup AGORA e planeje a troca.";
            else if (h == "Atencao") adv = "Ha sinais de desgaste, erros ou temperatura alta. Mantenha backup em dia e acompanhe.";
            else if (h == "Boa") adv = d.IsSsd ? "Tudo certo. SSDs perdem vida conforme a quantidade de dados gravados; valores abaixo de 30% pedem atencao." : "Tudo certo. Em HDDs, observe erros e temperatura (ideal abaixo de 50 C).";
            else adv = "O Windows nao informou S.M.A.R.T. deste disco (comum em discos USB e controladoras RAID).";
            lblAdvice.Text = adv; lblAdvice.ForeColor = h == "Ruim" || h == "Atencao" ? Xp.Bad : Xp.Dim;
        }

        void LiveTemp()
        {
            if (cur == null) return;
            double t = cur.Temp;
            foreach (Sn s in Sensors(cur)) if (s.Type == "Temperature" && s.IsRealTemp && s.Last.HasValue) { t = s.Last.Value; break; }
            if (double.IsNaN(t)) { pbTemp.Value = 0; lblTemp.Text = "indisponivel"; return; }
            pbTemp.Value = (int)Math.Max(0, Math.Min(100, t)); lblTemp.Text = t.ToString("0") + " C";
            lblTemp.ForeColor = t >= 70 ? Xp.Bad : Xp.GroupText;
        }
    }

    // ------------------------------------------------------------------
    //  Atualizacao de drivers: inventario (WMI) + Windows Update Agent (COM) com verificacao de assinatura e hash SHA-256
    // ------------------------------------------------------------------
    static class DriverUpdater
    {
        static object Gp(object o, string n) { return o.GetType().InvokeMember(n, BindingFlags.GetProperty, null, o, null); }
        static void Sp(object o, string n, object v) { o.GetType().InvokeMember(n, BindingFlags.SetProperty, null, o, new object[] { v }); }
        static object Cm(object o, string n, params object[] a) { return o.GetType().InvokeMember(n, BindingFlags.InvokeMethod | BindingFlags.GetProperty, null, o, a); }   // "Item" e propriedade indexada no COM
        static object New(string progId) { return Activator.CreateInstance(Type.GetTypeFromProgID(progId, true)); }
        static string S(object o) { return o == null ? "" : o.ToString().Trim(); }

        // desembrulha a excecao do COM e traduz os codigos mais comuns do Windows Update
        static string Why(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            int hr = Marshal.GetHRForException(ex);
            string hex = "0x" + hr.ToString("X8"), hint = "";
            switch ((uint)hr)
            {
                case 0x80070422: hint = "O servico Windows Update esta desativado."; break;
                case 0x8024402C: case 0x80072EE7: case 0x80072EFD: case 0x80072EE2: case 0x8024401C: hint = "Sem acesso ao servidor do Windows Update (internet, DNS, proxy ou firewall)."; break;
                case 0x80240438: case 0x8024001E: hint = "O servico Windows Update nao esta pronto; reinicie o Windows e tente de novo."; break;
                case 0x80070005: hint = "Acesso negado; execute o programa como administrador."; break;
                case 0x80240024: hint = "O Windows Update nao tem drivers para oferecer."; break;
                case 0x8024A105: case 0x80244022: case 0x80244010: hint = "O servidor do Windows Update esta ocupado; tente em alguns minutos."; break;
            }
            return ex.Message.Trim() + " (" + hex + ")" + (hint.Length > 0 ? " - " + hint : "");
        }

        // garante que o servico do Windows Update esteja ligado
        static void EnsureWuService(Action<string> log)
        {
            int c;
            string q = Proc.Run("sc.exe", "qc wuauserv", 15000, out c);
            if (q.IndexOf("DISABLED", StringComparison.OrdinalIgnoreCase) >= 0 || q.IndexOf("DESATIVADO", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                log("  O servico Windows Update esta desativado; ativando para esta operacao...");
                Proc.Run("sc.exe", "config wuauserv start= demand", 15000, out c);
            }
            Proc.Run("sc.exe", "start wuauserv", 15000, out c);
            Proc.Run("sc.exe", "start bits", 15000, out c);
        }

        static string LogPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BunoTunner_drivers.log"); } }
        static void ToFile(string s) { try { File.AppendAllText(LogPath, s + "\r\n", new UTF8Encoding(true)); } catch (Exception) { } }

        static string Ps(string script, int ms)
        {
            string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            int c;
            return Proc.Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc, ms, out c);
        }

        // inventario: todos os dispositivos com driver e os que estao com problema
        static void Inventory(Action<string> log)
        {
            int total = 0, unsigned = 0, bad = 0;
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT DeviceName, IsSigned FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL"))
                    foreach (ManagementBaseObject o in s.Get())
                    {
                        total++;
                        object sg = o["IsSigned"];
                        if (sg is bool && !(bool)sg) { unsigned++; log("  Sem assinatura: " + S(o["DeviceName"])); }
                    }
            }
            catch (Exception ex) { log("  (inventario WMI falhou: " + ex.Message + ")"); }
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
                    foreach (ManagementBaseObject o in s.Get())
                    {
                        bad++;
                        log("  Com problema (codigo " + S(o["ConfigManagerErrorCode"]) + "): " + S(o["Name"]));
                    }
            }
            catch (Exception) { }
            log("Dispositivos com driver: " + total + "   sem assinatura: " + unsigned + "   com problema: " + bad);
        }

        public class Found { public object Upd; public string Title, Maker, Cls, Date; }

        // Retorna true se terminou (ou se nao havia nada); confirm(lista) decide se instala
        public static void Run(bool install, Action<string> log, Func<string, bool> confirm, Action<string> done)
        {
            string result = "";
            try
            {
                DateTime started = DateTime.Now;
                ToFile("=== " + started.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
                Action<string> L = delegate(string s) { ToFile(s); log(s); };
                L("1/6  Analisando os dispositivos do sistema...");
                Inventory(L);

                L("2/6  Procurando drivers mais novos no Windows Update (pode levar alguns minutos)...");
                EnsureWuService(L);
                object session = New("Microsoft.Update.Session");
                object searcher = null, sr = null;
                int[] servers = { 2, 0 };   // primeiro direto no Windows Update (Microsoft), depois o padrao do PC
                string lastErr = "";
                foreach (int sv in servers)
                {
                    try
                    {
                        searcher = Cm(session, "CreateUpdateSearcher");
                        try { Sp(searcher, "ServerSelection", sv); } catch (Exception) { }
                        sr = Cm(searcher, "Search", "IsInstalled=0 and Type='Driver' and IsHidden=0");
                        break;
                    }
                    catch (Exception ex) { lastErr = Why(ex); L("  Busca falhou (" + (sv == 2 ? "Windows Update" : "servidor padrao") + "): " + lastErr); sr = null; }
                }
                if (sr == null) { result = "Nao foi possivel consultar o Windows Update: " + lastErr; L(result); done(result); return; }
                object ups = Gp(sr, "Updates");
                int n = Convert.ToInt32(Gp(ups, "Count"));
                if (n == 0) { result = "Todos os drivers ja estao na versao mais nova oferecida pelo Windows Update."; L(result); done(result); return; }

                List<Found> list = new List<Found>();
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < n; i++)
                {
                    object u = Cm(ups, "Item", i);
                    Found f = new Found(); f.Upd = u; f.Title = S(Gp(u, "Title")); f.Maker = S(Gp(u, "DriverManufacturer")); f.Cls = S(Gp(u, "DriverClass"));
                    try { f.Date = Convert.ToDateTime(Gp(u, "DriverVerDate")).ToString("yyyy-MM-dd"); } catch (Exception) { f.Date = ""; }
                    list.Add(f);
                    string line = "  " + f.Title + (f.Date.Length > 0 ? "  [" + f.Date + "]" : "");
                    L(line); sb.AppendLine("- " + f.Title + (f.Date.Length > 0 ? " (" + f.Date + ")" : ""));
                }
                L("Encontrados: " + n + " driver(s) mais novo(s).");
                if (!install) { result = "Verificacao concluida: " + n + " driver(s) podem ser atualizados."; done(result); return; }

                if (!confirm(sb.ToString())) { result = "Cancelado pelo usuario. Nada foi instalado."; L(result); done(result); return; }

                L("3/6  Criando ponto de restauracao do sistema...");
                string rp = Ps("try { Checkpoint-Computer -Description 'BunoTunner - antes de atualizar drivers' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; 'OK' } catch { 'FALHA: ' + $_.Exception.Message }", 180000);
                L(rp.Contains("OK") ? "  Ponto de restauracao criado." : "  Nao foi possivel criar o ponto de restauracao (" + (rp.Length > 120 ? rp.Substring(0, 120) : rp) + "). Continuando.");

                L("4/6  Baixando os drivers...");
                object coll = New("Microsoft.Update.UpdateColl");
                foreach (Found f in list)
                {
                    try { if (!Convert.ToBoolean(Gp(f.Upd, "EulaAccepted"))) Cm(f.Upd, "AcceptEula"); } catch (Exception) { }
                    Cm(coll, "Add", f.Upd);
                }
                object dl = Cm(session, "CreateUpdateDownloader");
                Sp(dl, "Updates", coll);
                object dr = Cm(dl, "Download");
                int drc = Convert.ToInt32(Gp(dr, "ResultCode"));
                if (drc != 2 && drc != 3) { result = "Falha no download (codigo " + drc + "). Nada foi instalado."; L(result); done(result); return; }

                L("5/6  Verificando assinatura digital e hash SHA-256 dos arquivos baixados...");
                string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SoftwareDistribution\Download");
                string script = "$ErrorActionPreference='SilentlyContinue'; $t=[datetime]'" + started.AddSeconds(-5).ToString("yyyy-MM-dd HH:mm:ss") + "'; " +
                    "Get-ChildItem -LiteralPath '" + cache + "' -Recurse -File | Where-Object { $_.LastWriteTime -ge $t } | ForEach-Object { " +
                    "$s=(Get-AuthenticodeSignature -LiteralPath $_.FullName).Status.ToString(); $h=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; $s+'|'+$h+'|'+$_.Name }";
                string vout = Ps(script, 600000);
                int ok = 0, tampered = 0, other = 0;
                foreach (string ln in vout.Split('\n'))
                {
                    string[] p = ln.Trim().Split('|');
                    if (p.Length < 3) continue;
                    ToFile("  SHA256 " + p[1] + "  " + p[2] + "  assinatura: " + p[0]);
                    if (p[0] == "Valid") ok++;
                    else if (p[0] == "HashMismatch" || p[0] == "NotTrusted") { tampered++; L("  ALERTA: " + p[2] + " -> " + p[0]); }
                    else other++;
                }
                L("  Assinaturas validas: " + ok + "   sem assinatura propria (verificados pelo Windows Update): " + other + "   alterados/nao confiaveis: " + tampered);
                if (tampered > 0) { result = "ABORTADO: " + tampered + " arquivo(s) com hash/assinatura invalidos. Nada foi instalado."; L(result); done(result); return; }

                L("6/6  Instalando...");
                object inst = Cm(session, "CreateUpdateInstaller");
                Sp(inst, "Updates", coll);
                try { Sp(inst, "AllowSourcePrompts", false); } catch (Exception) { }
                try { Sp(inst, "ForceQuiet", true); } catch (Exception) { }
                object ir = Cm(inst, "Install");
                int good = 0, fail = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    object ur = Cm(ir, "GetUpdateResult", i);
                    int rc = Convert.ToInt32(Gp(ur, "ResultCode"));
                    if (rc == 2 || rc == 3) { good++; L("  Instalado: " + list[i].Title); }
                    else { fail++; L("  FALHOU (codigo " + rc + ", 0x" + Convert.ToInt32(Gp(ur, "HResult")).ToString("X8") + "): " + list[i].Title); }
                }
                bool reboot = false;
                try { reboot = Convert.ToBoolean(Gp(ir, "RebootRequired")); } catch (Exception) { }
                result = "Concluido: " + good + " instalado(s), " + fail + " com falha." + (reboot ? " REINICIE o Windows para terminar." : "");
                L(result);
                done(result);
            }
            catch (Exception ex)
            {
                result = "Erro: " + Why(ex);
                log(result); ToFile(result);
                done(result);
            }
        }
    }

    // ------------------------------------------------------------------
    //  Otimizacoes do Windows: limpeza de temporarios (CacheCleaner)
    // ------------------------------------------------------------------
    class OptPage : Panel
    {
        // cada otimizacao e uma linha padrao: titulo, descricao, botao principal (e opcional de desfazer) e o resultado
        int top;
        Panel box = new Panel();

        public OptPage()
        {
            Dock = DockStyle.Fill;
            box.Dock = DockStyle.Fill; box.AutoScroll = true; Controls.Add(box);
            AddDrivers();
            Add("Limpar arquivos temporarios", "Apaga C:\\Windows\\Temp, Prefetch e a pasta %temp% do usuario (itens em uso sao ignorados). Nenhum documento pessoal e tocado.",
                "Limpar", delegate { return Clean(); }, null, null, null, null);
            Add("Desativar compressao de memoria", "Executa Disable-MMAgent -mc: o Windows deixa de comprimir a RAM (usa um pouco menos de CPU, um pouco mais de RAM). Vale apos reiniciar o PC.",
                "Desativar", delegate { return Ps("Disable-MMAgent -mc", "Compressao de memoria desativada. Reinicie o Windows para valer."); },
                "Reativar", delegate { return Ps("Enable-MMAgent -mc", "Compressao de memoria reativada. Reinicie o Windows para valer."); },
                delegate { int c; string o = Proc.Run("powershell.exe", "-NoProfile -NonInteractive -Command \"(Get-MMAgent).MemoryCompression\"", 20000, out c); return o.Trim() == "True" ? "Estado: compressao ATIVADA" : o.Trim() == "False" ? "Estado: compressao DESATIVADA" : ""; }, null);
            const string ClickKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
            Add("Menu de clique direito do Windows 10", "No Windows 11, traz de volta o menu de clique direito completo e classico do Windows 10 (sem o \"Mostrar mais opcoes\"). Vale ao reiniciar o Explorer ou fazer logoff.",
                "Ativar", delegate { using (RegistryKey k = Root(false).CreateSubKey(ClickKey + "\\InprocServer32")) { k.SetValue("", ""); } return "Menu classico ativado. Reinicie o Explorer ou faca logoff."; },
                "Desfazer", delegate { Root(false).DeleteSubKeyTree(ClickKey, false); return "Menu padrao do Windows 11 restaurado. Reinicie o Explorer ou faca logoff."; },
                delegate { using (RegistryKey k = Root(false).OpenSubKey(ClickKey + "\\InprocServer32")) return k != null ? "Estado: menu classico ATIVO" : "Estado: menu padrao do Windows 11"; }, null);
            Add("Desativar MPO (Multiplane Overlay)", "Define OverlayTestMode=5 no DWM. Ajuda a resolver tremidas, telas piscando e telas pretas em alguns monitores/placas NVIDIA e AMD. Reinicie o PC para valer.",
                "Desativar MPO", delegate { Sub(true, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5); return "MPO desativado. Reinicie o PC."; },
                "Reativar", delegate { Del(true, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode"); return "MPO restaurado ao padrao. Reinicie o PC."; },
                delegate { object v = Get(true, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode"); return v != null && Convert.ToInt32(v) == 5 ? "Estado: MPO DESATIVADO" : "Estado: MPO padrao (ativo)"; }, null);
            Add("Overlay DLSS (indicador na tela)", "Mostra no canto do jogo um indicador quando o DLSS esta em uso (so placas NVIDIA RTX). Aplica na proxima vez que abrir o jogo.",
                "Ativar", delegate { Sub(true, @"SOFTWARE\NVIDIA Corporation\Global\NGXCore", "ShowDlssIndicator", 0x400); return "Overlay do DLSS ativado."; },
                "Desativar", delegate { Sub(true, @"SOFTWARE\NVIDIA Corporation\Global\NGXCore", "ShowDlssIndicator", 0); return "Overlay do DLSS desativado."; },
                delegate { object v = Get(true, @"SOFTWARE\NVIDIA Corporation\Global\NGXCore", "ShowDlssIndicator"); return v != null && Convert.ToInt32(v) == 0x400 ? "Estado: overlay ATIVO" : "Estado: overlay desligado"; }, null);
            const string HvKey = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
            Add("Isolamento de nucleo (Integridade de memoria)", "Recurso de seguranca do Windows (HVCI) que isola o nucleo do sistema usando virtualizacao. Desativar pode dar mais FPS em jogos e liberar drivers antigos, mas reduz a protecao. Reinicie o PC.",
                "Desativar", delegate { Sub(true, HvKey, "Enabled", 0); return "Isolamento de nucleo DESATIVADO. Reinicie o PC."; },
                "Ativar", delegate { Sub(true, HvKey, "Enabled", 1); return "Isolamento de nucleo ATIVADO. Reinicie o PC."; },
                delegate { object v = Get(true, HvKey, "Enabled"); return v != null && Convert.ToInt32(v) == 1 ? "Estado: ATIVADO" : "Estado: desativado"; },
                "Desativar o Isolamento de nucleo reduz a protecao contra malware que ataca o nucleo do Windows. Desativar mesmo assim?");
            Add("Hibernacao", "Liga ou desliga a hibernacao (powercfg /hibernate). Desligada, apaga o hiberfil.dat (libera varios GB no disco) e tambem desativa a Inicializacao Rapida.",
                "Desativar", delegate { return Pc("off", "Hibernacao DESATIVADA (hiberfil.sys removido)."); },
                "Ativar", delegate { return Pc("on", "Hibernacao ATIVADA."); },
                delegate { object v = Get(true, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled"); return v != null && Convert.ToInt32(v) == 0 ? "Estado: DESATIVADA" : "Estado: ativada"; }, null);
            Add("Aceleracao do mouse (Aprimorar precisao do ponteiro)", "Desligada, o ponteiro anda sempre a mesma distancia para o mesmo movimento da mao (recomendado para jogos). Aplica na hora, sem reiniciar.",
                "Desativar", delegate { return Mouse(false); },
                "Ativar", delegate { return Mouse(true); },
                delegate { using (RegistryKey k = Root(false).OpenSubKey(@"Control Panel\Mouse")) { object v = k == null ? null : k.GetValue("MouseSpeed"); return v != null && v.ToString() != "0" ? "Estado: ATIVADA" : "Estado: desativada"; } }, null);
            const string MmKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
            Add("Mitigacoes do Windows (Spectre / Meltdown)", "Desativa as mitigacoes de execucao especulativa (FeatureSettingsOverride=3) para ganhar desempenho. REDUZ A SEGURANCA do PC; use so em maquina de jogos sem dados sensiveis. Reinicie o PC.",
                "Desativar", delegate { Sub(true, MmKey, "FeatureSettingsOverride", 3); Sub(true, MmKey, "FeatureSettingsOverrideMask", 3); return "Mitigacoes DESATIVADAS. Reinicie o PC."; },
                "Padrao do Windows", delegate { Del(true, MmKey, "FeatureSettingsOverride"); Del(true, MmKey, "FeatureSettingsOverrideMask"); Del(true, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization", "MinVmVersionForCpuBasedMitigations"); return "Mitigacoes restauradas ao padrao do Windows. Reinicie o PC."; },
                delegate { object v = Get(true, MmKey, "FeatureSettingsOverride"); return v != null ? "Estado: personalizado (FeatureSettingsOverride=" + v + ")" : "Estado: padrao do Windows (mitigacoes ativas)"; },
                "ATENCAO: desativar as mitigacoes de Spectre/Meltdown deixa o PC mais vulneravel a ataques que leem memoria (por exemplo, por codigo malicioso no navegador). O ganho de desempenho costuma ser pequeno em CPUs recentes.\n\nDesativar mesmo assim?");
        }

        // primeiro item da lista: atualiza os drivers de todos os dispositivos
        void AddDrivers()
        {
            XpGroup g = new XpGroup(); g.Title = "Atualizar drivers de todos os dispositivos"; g.SetBounds(0, 0, 650, 246);
            Label d = UI.Lbl(g, "Analisa todos os dispositivos do PC e procura no Windows Update os drivers mais novos. Antes de instalar, cria um ponto de restauracao e confere a assinatura digital e o hash SHA-256 dos arquivos baixados; se algum estiver alterado, nada e instalado.", 12, 22, Xp.Ui, Xp.Dim);
            d.AutoSize = false; d.SetBounds(12, 22, 624, 44);
            XpButton bGo = UI.Btn(g, "Atualizar drivers", 12, 70, 150, null);
            XpButton bScan = UI.Btn(g, "So verificar", 170, 70, 120, null);
            bGo.Height = 24; bScan.Height = 24;
            XpProgress pb = new XpProgress(); pb.SetBounds(300, 75, 336, 14); g.Controls.Add(pb);
            TextBox log = new TextBox(); log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.WordWrap = true;
            log.Font = Xp.Ui; log.BackColor = Xp.EditBg; log.ForeColor = Xp.Text; log.SetBounds(12, 100, 624, 138);
            log.Text = "Clique em 'Atualizar drivers' para analisar e instalar, ou em 'So verificar' para apenas listar o que esta desatualizado.";
            g.Controls.Add(log);
            Timer anim = new Timer(); anim.Interval = 60; anim.Tick += delegate { pb.Step(); };
            Action<bool> start = delegate(bool install)
            {
                bGo.Enabled = false; bScan.Enabled = false; pb.Marquee = true; anim.Start(); log.Text = "";
                Action<string> add = delegate(string s) { try { BeginInvoke(new Action(delegate { log.AppendText(Lang.T(s) + "\r\n"); })); } catch (Exception) { } };
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    DriverUpdater.Run(install, add,
                        delegate(string lst)
                        {
                            bool yes = false;
                            try
                            {
                                Invoke(new Action(delegate
                                {
                                    yes = Dlg.Show(this, "Estes drivers serao baixados, verificados (assinatura e hash SHA-256) e instalados:\n\n" + lst +
                                        "\nSera criado um ponto de restauracao antes. A tela pode piscar (driver de video) e pode ser preciso reiniciar o Windows.\n\nContinuar?",
                                        "BunoTunner - Otimizacoes", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;
                                }));
                            }
                            catch (Exception) { }
                            return yes;
                        },
                        delegate(string r)
                        {
                            try { BeginInvoke(new Action(delegate { anim.Stop(); pb.Marquee = false; pb.Value = 0; pb.Invalidate(); bGo.Enabled = true; bScan.Enabled = true; })); } catch (Exception) { }
                        });
                });
            };
            bGo.Click += delegate { start(true); };
            bScan.Click += delegate { start(false); };
            box.Controls.Add(g);
            top = 254;
        }

        static RegistryKey Root(bool machine) { return RegistryKey.OpenBaseKey(machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64); }
        static void Sub(bool machine, string path, string name, int val) { using (RegistryKey k = Root(machine).CreateSubKey(path)) k.SetValue(name, val, RegistryValueKind.DWord); }
        static void Del(bool machine, string path, string name) { using (RegistryKey k = Root(machine).OpenSubKey(path, true)) if (k != null) k.DeleteValue(name, false); }
        static object Get(bool machine, string path, string name) { using (RegistryKey k = Root(machine).OpenSubKey(path)) return k == null ? null : k.GetValue(name); }

        [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint action, uint param, int[] vparam, uint winIni);

        static string Pc(string onOff, string ok)
        {
            int c; string o = Proc.Run("powercfg.exe", "/hibernate " + onOff, 30000, out c);
            return c == 0 ? ok : "Falhou (execute como administrador): " + o;
        }

        // MouseSpeed/Threshold = aceleracao do Windows; SPI_SETMOUSE aplica na hora
        static string Mouse(bool on)
        {
            string sp = on ? "1" : "0", t1 = on ? "6" : "0", t2 = on ? "10" : "0";
            using (RegistryKey k = Root(false).CreateSubKey(@"Control Panel\Mouse"))
            {
                k.SetValue("MouseSpeed", sp, RegistryValueKind.String);
                k.SetValue("MouseThreshold1", t1, RegistryValueKind.String);
                k.SetValue("MouseThreshold2", t2, RegistryValueKind.String);
            }
            SystemParametersInfo(0x0004, 0, new int[] { int.Parse(t1), int.Parse(t2), int.Parse(sp) }, 3);
            return on ? "Aceleracao do mouse ATIVADA." : "Aceleracao do mouse DESATIVADA.";
        }

        static string Ps(string cmd, string ok)
        {
            int c;
            string o = Proc.Run("powershell.exe", "-NoProfile -NonInteractive -Command \"" + cmd + "\"", 60000, out c);
            return c == 0 ? ok : "Falhou (execute como administrador): " + o;
        }

        static string Clean()
        {
            string tmp = Environment.GetEnvironmentVariable("TEMP") ?? "";
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] dirs = { Path.Combine(win, "Temp"), Path.Combine(win, "Prefetch"), tmp };
            long freed = 0; int files = 0;
            foreach (string d in dirs)
            {
                if (d.Length < 8 || !Directory.Exists(d)) continue;
                try
                {
                    foreach (string f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                        try { long sz = new FileInfo(f).Length; File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); freed += sz; files++; } catch (Exception) { }
                    string[] subs = Directory.GetDirectories(d, "*", SearchOption.AllDirectories);
                    Array.Sort(subs, delegate(string x, string y) { return y.Length.CompareTo(x.Length); });
                    foreach (string sd in subs) try { Directory.Delete(sd); } catch (Exception) { }
                }
                catch (Exception) { }
            }
            return "Concluido: " + files + " arquivo(s) apagado(s), " + (freed / 1048576.0).ToString("0.0") + " MB liberados.";
        }

        void Add(string title, string desc, string runText, Func<string> run, string undoText, Func<string> undo, Func<string> state, string confirm)
        {
            XpGroup g = new XpGroup(); g.Title = title; g.SetBounds(0, top, 650, 78); top += 84;
            Label d = UI.Lbl(g, desc, 12, 22, Xp.Ui, Xp.Dim);
            d.AutoSize = false; d.SetBounds(12, 22, 500, 32);
            Label st = UI.Lbl(g, "", 12, 56, Xp.Ui, Xp.GroupText);
            st.AutoSize = false; st.SetBounds(12, 56, 500, 18);
            if (state != null) System.Threading.ThreadPool.QueueUserWorkItem(delegate { string t; try { t = state(); } catch (Exception) { t = ""; } try { BeginInvoke(new Action(delegate { if (st.Text.Length == 0) st.Text = t; })); } catch (Exception) { } });
            XpButton b1 = UI.Btn(g, runText, 524, 22, 112, null);
            XpButton b2 = undoText != null ? UI.Btn(g, undoText, 524, 50, 112, null) : null;
            b1.Height = 24;
            Action<Func<string>> go = delegate(Func<string> f)
            {
                b1.Enabled = false; if (b2 != null) b2.Enabled = false; st.Text = "Executando...";
                Func<string> fin = f;
                if (state != null) f = delegate { string r0 = fin(); string t = ""; try { t = state(); } catch (Exception) { } return t.Length > 0 ? r0 + "  [" + t + "]" : r0; };
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    string r;
                    try { r = f(); } catch (Exception ex) { r = "Erro: " + ex.Message; }
                    try { BeginInvoke(new Action(delegate { st.Text = r; b1.Enabled = true; if (b2 != null) b2.Enabled = true; })); } catch (Exception) { }
                });
            };
            b1.Click += delegate { if (confirm != null && Dlg.Show(confirm, "BunoTunner - Otimizacoes", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return; go(run); };
            if (b2 != null) b2.Click += delegate { go(undo); };
            box.Controls.Add(g);
        }
    }

    class LatGraph : Control
    {
        public List<double> Kb = new List<double>(), Ns = new List<double>();
        public LatGraph() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Xp.GraphBg);
            Rectangle r = new Rectangle(34, 6, Width - 42, Height - 22);
            using (Pen gp = new Pen(Xp.GraphGrid)) { for (int i = 0; i <= 4; i++) g.DrawLine(gp, r.X, r.Y + r.Height * i / 4, r.Right, r.Y + r.Height * i / 4); }
            double mx = 10; foreach (double v in Ns) if (v > mx) mx = v;
            mx = Math.Ceiling(mx / 10) * 10;
            using (Font f = new Font(Xp.Ui.FontFamily, 7f))
            {
                for (int i = 0; i <= 4; i++) TextRenderer.DrawText(g, (mx * (4 - i) / 4).ToString("0"), f, new Point(2, r.Y + r.Height * i / 4 - 6), Xp.GraphText);
                string[] lab = { "4K", "32K", "256K", "2M", "16M", "128M" }; int[] idx = { 0, 3, 6, 9, 12, 15 };
                for (int i = 0; i < lab.Length; i++) TextRenderer.DrawText(g, lab[i], f, new Point(r.X + r.Width * idx[i] / 16 - 8, r.Bottom + 1), Xp.GraphText);
                if (Ns.Count == 0) TextRenderer.DrawText(g, Lang.T("ns x tamanho do bloco"), f, new Point(r.X + 8, r.Y + 4), Xp.GraphText);
            }
            if (Ns.Count > 1)
            {
                PointF[] pts = new PointF[Ns.Count];
                for (int i = 0; i < Ns.Count; i++) pts[i] = new PointF(r.X + r.Width * i / 16f, r.Bottom - (float)(Ns[i] / mx) * r.Height);
                using (Pen lp = new Pen(Color.FromArgb(40, 190, 90), 2f)) g.DrawLines(lp, pts);
                foreach (PointF p in pts) g.FillEllipse(Brushes.Orange, p.X - 2.5f, p.Y - 2.5f, 5, 5);
            }
            using (Pen bp = new Pen(Xp.GraphGrid)) g.DrawRectangle(bp, r);
        }
    }

    static class UI
    {
        public static Label Lbl(Control p, string t, int x, int y, Font f = null, Color? c = null)
        {
            Label l = new Label();
            l.AutoSize = true; l.BackColor = Color.Transparent; l.Text = t;
            l.Font = f ?? Xp.Ui; l.ForeColor = c ?? Xp.Text; l.Location = new Point(x, y);
            p.Controls.Add(l);
            return l;
        }
        public static XpButton Btn(Control p, string t, int x, int y, int w, EventHandler h)
        {
            XpButton b = new XpButton(); b.Text = t; b.SetBounds(x, y, w, 24);
            if (h != null) b.Click += h;
            p.Controls.Add(b);
            return b;
        }
        public static XpText Txt(Control p, int x, int y, int w, string t)
        {
            XpText b = new XpText(); b.Location = new Point(x, y); b.Width = w; b.Text = t;
            p.Controls.Add(b);
            return b;
        }
        public static XpOption Opt(Control p, string t, int x, int y, int w, bool radio)
        {
            XpOption o = new XpOption(); o.Text = t; o.Radio = radio; o.SetBounds(x, y, w, 18);
            p.Controls.Add(o);
            return o;
        }
        public static void Bind(XpSlider s, XpText t)
        {
            s.ValueChanged += delegate { string v = s.Value.ToString(); if (t.Text != v) t.Text = v; };
            EventHandler commit = delegate { int v; if (int.TryParse(t.Text.Trim(), out v)) s.Value = v; t.Text = s.Value.ToString(); };
            t.Box.Leave += commit;
            t.Box.KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; commit(o, EventArgs.Empty); }
            };
        }
        public static int Int(XpText t, int def) { int v; return int.TryParse(t.Text.Trim(), out v) ? v : def; }
        public static void DoubleBuffer(Control c)
        {
            try { typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true, null); } catch (Exception) { }
        }
    }

    // ------------------------------------------------------------------
    //  Janela principal
    // ------------------------------------------------------------------
    class MainForm : XpForm
    {
        ComboBox cmb = new ComboBox();
        Gpu cur;
        Panel pgFan, pgOc, pgSpec, pgCpu, pgBench, pgRam, pgAff, pgUnin, pgNet, pgSto, pgOpt;
        FanPage fanPage; RamPage ramPage; AffinityPage affPage;
        XpIconButton btnRam, btnAff, btnUnin, btnNet, btnSto, btnOpt;
        Label lblGpuSel;
        ListView lvCpu = new ListView();
        List<ListViewItem> cpuLive = new List<ListViewItem>();
        Timer timer = new Timer();
        Label status = new Label();
        string statusText;
        SensorForm sensForm;
        XpIconButton btnFan, btnOc, btnSpec, btnCpu, btnBench;
        LTip tip = new LTip();
        // benchmark
        ComboBox cbBThr, cbBRam, cbBDrive, cbBSize, cbTheme;
        Label lblTheme, lblBRate, lblBAvg, lblBPeak, lblBTemp, lblBErr, lblBTime, lblBMsg;
        XpOption chkBSafe; XpText txBTemp; XpProgress pbBench;
        XpGraph grBench = new XpGraph(); ListView lvBench = new ListView(); Timer tmBench = new Timer();
        Sn bsnRate, bsnTemp; float? bRateNow, bTempNow;
        RenderForm rf; bool bExt; string bU = ""; double bSc = 1; Action bFallback; bool bRunning; int bKind, bDurSec, bN, gpuGen; long bLast; DateTime bStart, bLastT; double bSum, bPeak, bTempMax;
        static readonly string[] BName = { "GPU", "CPU", "FPU", "RAM", "SSD" };
        static readonly string[] BUnit = { "GFLOPS", "Mrays/s", "GFLOPS", "GB/s", "MB/s" };
        static readonly double[] BScale = { 1e-9, 1e-6, 1e-9, 1e-9, 1e-6 };
        const string PayPalUrl = "https://www.paypal.com/donate/?business=LYD3FYNZCDRLE&no_recurring=0&item_name=Thanks%21&currency_code=BRL&source=qr";
        const string LivePixUrl = "https://livepix.gg/buno";

        // overclock
        XpSlider slCore, slMem, slPow, slTemp;
        XpText txCore, txMem, txPow, txTemp, txLockC1, txLockC2, txLockM1, txLockM2;
        Label rgCore, rgMem, rgPow, rgTemp, lblOcMsg, lblClocks, lblUCore, lblUMem, lblUPow, lblUTemp, lblOcInfo;
        XpGroup grpOc; XpButton btnOcApply, btnOcReset;
        List<Control> ocLock = new List<Control>();
        // specs
        ListView lvSpec = new ListView();

        public MainForm()
        {
            Text = "BunoTunner";
            Size = new Size(772, 706);

            Startup.Mark("MainForm: inicio");
            SensorHub.Open();
            Startup.Mark("SensorHub.Open concluido");

            tip.InitialDelay = 200; tip.ReshowDelay = 100; tip.AutoPopDelay = 8000;

            Panel right = new Panel(); right.Dock = DockStyle.Fill; right.Padding = new Padding(8);
            TaskPane left = new TaskPane(); left.Dock = DockStyle.Left; left.Width = 72;
            Panel bar = new Panel(); bar.Dock = DockStyle.Bottom; bar.Height = 30; bar.BackColor = Xp.Face;
            status.AutoSize = false; status.BackColor = Color.Transparent; status.TextAlign = ContentAlignment.MiddleLeft;
            statusText = SensorHub.Status(); status.SetBounds(6, 7, 250, 16); status.Text = statusText;
            bar.Controls.Add(status);

            // doacoes (canto inferior direito)
            XpIconButton bPay = new XpIconButton("paypal", 24), bLive = new XpIconButton("livepix", 24);
            bPay.Top = 3; bLive.Top = 3;
            bar.Controls.Add(bPay); bar.Controls.Add(bLive);
            EventHandler place = delegate { bLive.Left = bar.Width - 32; bPay.Left = bar.Width - 62; };
            bar.Resize += place; place(null, EventArgs.Empty);
            bPay.Click += delegate { Donate(PayPalUrl, "PayPal"); };
            bLive.Click += delegate { Donate(LivePixUrl, "LivePix"); };
            tip.SetToolTip(bPay, "Apoie o BunoTunner via PayPal");
            tip.SetToolTip(bLive, "Apoie o BunoTunner via LivePix");

            Controls.Add(right);
            Controls.Add(left);
            Controls.Add(bar);

            // painel lateral: so icones (a descricao aparece ao passar o mouse)
            btnFan = IconBtn(left, "fan", 12, 262, "Ventoinhas\nAutomatico, manual (% e RPM) e curva de temperatura", delegate { ShowPage(pgFan); });
            btnOc = IconBtn(left, "oc", 12, 312, "Overclock\nCore clock, memory clock, TDP, limite de temperatura e travar clocks", delegate { ShowPage(pgOc); });
            btnSpec = IconBtn(left, "gpu", 12, 62, "Especificacoes da GPU\nTodos os dados da placa de video", delegate { ShowPage(pgSpec); });
            btnCpu = IconBtn(left, "chip", 12, 12, "Especificacoes da CPU\nProcessador, nucleos, cache, clocks e conjuntos de instrucoes", delegate { ShowPage(pgCpu); });
            btnBench = IconBtn(left, "bench", 12, 362, "Benchmark / Estresse\nGPU, CPU, FPU, RAM e SSD com verificacao de erros e limite de temperatura", delegate { ShowPage(pgBench); });
            btnRam = IconBtn(left, "ram", 12, 112, "Memoria RAM\nVelocidade, canais, voltagem, modulos e timings basicos (tCAS, tRCD, tRP, tRAS, tRC, tRFC, Command Rate)", delegate { ShowPage(pgRam); });
            btnSto = IconBtn(left, "health", 12, 162, "Saude do armazenamento\nVida util restante, temperatura e S.M.A.R.T. de SSDs NVMe/SATA e HDs", delegate { ShowPage(pgSto); });
            btnNet = IconBtn(left, "net", 12, 212, "Internet\nTeste de velocidade (ping, download, upload) e informacoes da rede", delegate { ShowPage(pgNet); });
            btnAff = IconBtn(left, "aff", 12, 412, "Afinidade de processos\nEscolha em quais nucleos cada programa roda; presets e regras automaticas", delegate { ShowPage(pgAff); });
            btnUnin = IconBtn(left, "trash", 12, 462, "Desinstalador\nRemove programas por completo: pastas, registro, inicializacao, servicos e tarefas que sobram; tambem remove o que se recusa a desinstalar", delegate { ShowPage(pgUnin); });
            btnOpt = IconBtn(left, "opt", 12, 512, "Otimizacoes do Windows\nLimpeza de temporarios, compressao de memoria e outros ajustes", delegate { ShowPage(pgOpt); });
            IconBtn(left, "chart", 12, 562, "Sensores\nTudo em uma janela: GPU, CPU, placa-mae, memoria, discos e ventoinhas, com grafico, busca e alertas", delegate { OpenSensors(); });

            // topo: escolha da GPU
            lblGpuSel = UI.Lbl(right, "Placa de video:", 0, 6, Xp.UiBold);
            cmb.DropDownStyle = ComboBoxStyle.DropDownList; cmb.Font = Xp.Ui;
            cmb.SetBounds(95, 2, 330, 22);
            right.Controls.Add(cmb);
            lblTheme = UI.Lbl(bar, "Tema:", 264, 8, Xp.UiBold);
            cbTheme = new ComboBox(); cbTheme.DropDownStyle = ComboBoxStyle.DropDownList; cbTheme.Font = Xp.Ui; cbTheme.SetBounds(306, 4, 176, 22); cbTheme.DropDownWidth = 230;
            cbTheme.Items.AddRange(Xp.ThemeNames);
            cbTheme.SelectedIndex = Xp.Theme;
            bar.Controls.Add(cbTheme);
            UI.Lbl(bar, "Idioma:", 492, 8, Xp.UiBold);
            ComboBox cbLang = new ComboBox(); cbLang.DropDownStyle = ComboBoxStyle.DropDownList; cbLang.Font = Xp.Ui; cbLang.SetBounds(544, 4, 140, 22); cbLang.DropDownWidth = 170;
            cbLang.Items.AddRange(new object[] { "Auto (Windows)", "Portugu\u00EAs (Brasil)", "English" });
            string lsv = Settings.Get("lang", "auto");
            cbLang.SelectedIndex = lsv == "pt" ? 1 : lsv == "en" ? 2 : 0;
            bar.Controls.Add(cbLang);
            cbLang.Tag = "nolang"; cbTheme.Tag = "nolang";
            cbLang.SelectedIndexChanged += delegate
            {
                Settings.Set("lang", cbLang.SelectedIndex == 1 ? "pt" : cbLang.SelectedIndex == 2 ? "en" : "auto");
                Lang.Init();
                Lang.Reapply();
            };
            cbTheme.SelectedIndexChanged += delegate
            {
                Xp.SetTheme(cbTheme.SelectedIndex);
                Settings.Set("theme", Xp.ThemeIds[cbTheme.SelectedIndex]);
                Xp.ApplyAll();
            };
            tip.SetToolTip(cbTheme, "Muda o visual do programa: Luna (XP), Frutiger Aero (Win7), Dark/AMOLED, Neon, Steampunk ou Vintage (Windows 95)");
            foreach (Gpu g in SensorHub.Gpus) cmb.Items.Add(g);

            Panel host = new Panel(); host.SetBounds(0, 34, 676, 586);
            host.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            right.Controls.Add(host);
            fanPage = new FanPage(); pgFan = fanPage; pgOc = BuildOcPage(); pgSpec = BuildSpecPage(); pgCpu = BuildCpuPage(); pgBench = BuildBenchPage();
            ramPage = new RamPage(); pgRam = ramPage; affPage = new AffinityPage(); pgAff = affPage; pgUnin = new UninstallPage(); pgNet = new NetPage(); pgSto = new StoragePage(); pgOpt = new OptPage();
            host.Controls.Add(pgFan); host.Controls.Add(pgOc); host.Controls.Add(pgSpec); host.Controls.Add(pgCpu); host.Controls.Add(pgBench); host.Controls.Add(pgRam); host.Controls.Add(pgAff); host.Controls.Add(pgUnin); host.Controls.Add(pgNet); host.Controls.Add(pgSto); host.Controls.Add(pgOpt);
            Shown += delegate
            {
                Startup.Mark("janela mostrada");
                Lang.Apply(this, tip);
                Lang.StartSweeper();
                Startup.CloseSplash();
                Startup.Save();
                StartGpuExtras();
                ramPage.LoadAsync();
                // WMI e lento: coleta a CPU em segundo plano para nao travar a janela
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    List<string[]> rows = CpuInfo.Collect();
                    try { BeginInvoke(new Action(delegate { FillCpu(rows); })); } catch (Exception) { }
                });
            };
            Startup.Mark("MainForm: paginas criadas");
            ShowPage(pgCpu);

            cmb.SelectedIndexChanged += delegate { LoadGpu(); };
            if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
            else
            {
                Dlg.Show("Nenhuma placa de video foi detectada.\nRode como administrador e confira a pasta libs.", "BunoTunner");
            }

            timer.Interval = 1000;
            timer.Tick += delegate { try { Tick(); } catch (Exception) { } };
            timer.Start();
            FormClosing += OnClosing;
            InitTray();
        }

        XpIconButton IconBtn(Control parent, string kind, int x, int y, string tipText, EventHandler h)
        {
            XpIconButton b = new XpIconButton(kind, 48);
            b.Location = new Point(x, y);
            b.Click += h;
            parent.Controls.Add(b);
            tip.SetToolTip(b, tipText);
            return b;
        }

        void Donate(string url, string nome)
        {
            if (string.IsNullOrEmpty(url)) { Dlg.Show("O link do " + nome + " ainda nao foi configurado.\nEm breve!", "BunoTunner"); return; }
            try { Process.Start(url); } catch (Exception ex) { Dlg.Show(ex.Message, "BunoTunner"); }
        }

        void ShowPage(Panel p)
        {
            Panel[] pages = { pgFan, pgOc, pgSpec, pgCpu, pgBench, pgRam, pgAff, pgUnin, pgNet, pgSto, pgOpt };
            XpIconButton[] btns = { btnFan, btnOc, btnSpec, btnCpu, btnBench, btnRam, btnAff, btnUnin, btnNet, btnSto, btnOpt };
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Visible = p == pages[i];
                if (btns[i] != null) { btns[i].Selected = p == pages[i]; btns[i].Invalidate(); }
            }
            lblGpuSel.Visible = cmb.Visible = p != pgCpu && p != pgRam && p != pgAff && p != pgUnin && p != pgNet && p != pgSto && p != pgOpt;
        }

        void OpenSensors()
        {
            if (sensForm == null || sensForm.IsDisposed)
            {
                List<Sn> all = new List<Sn>();
                all.AddRange(SensorHub.GpuSensors); all.AddRange(SensorHub.PcSensors);
                sensForm = new SensorForm("BunoTunner - Sensores (GPU, CPU, placa-mae, memoria, discos, ventoinhas...)", all);
                sensForm.Show();
            }
            else { if (sensForm.WindowState == FormWindowState.Minimized) sensForm.WindowState = FormWindowState.Normal; sensForm.BringToFront(); }
        }

        void OcRow(Control parent, string title, int y, XpSlider s, XpText t, out Label unit, out Label range)
        {
            UI.Lbl(parent, title, 14, y, Xp.UiBold);
            s.SetBounds(14, y + 14, 400, 34); parent.Controls.Add(s);
            t.Location = new Point(424, y + 20); t.Width = 56; parent.Controls.Add(t);
            unit = UI.Lbl(parent, "MHz", 486, y + 24);
            range = UI.Lbl(parent, "", 530, y + 24, Xp.Ui, Xp.Dim);
            UI.Bind(s, t);
        }

        Panel BuildOcPage()
        {
            Panel p = new Panel(); p.Dock = DockStyle.Fill;
            XpGroup g = new XpGroup(); g.Title = "Overclock / limites"; g.SetBounds(0, 0, 676, 436);
            grpOc = g;
            Label warn = new Label();
            warn.SetBounds(14, 22, 648, 34); warn.Tag = "warn"; warn.BackColor = Xp.EditBg; warn.BorderStyle = BorderStyle.FixedSingle;
            warn.ForeColor = Xp.Text;
            warn.Text = "  AVISO: overclock pode causar travamentos e artefatos. Va aos poucos e teste a estabilidade.\n  Vale ate reiniciar ou clicar em Redefinir. Funciona em NVIDIA, AMD (Overdrive8) e Intel Arc.";
            warn.Font = Xp.Ui; g.Controls.Add(warn);

            slCore = new XpSlider(); txCore = new XpText();
            slMem = new XpSlider(); txMem = new XpText();
            slPow = new XpSlider(); txPow = new XpText();
            slTemp = new XpSlider(); txTemp = new XpText();
            OcRow(g, "Core Clock (offset)", 66, slCore, txCore, out lblUCore, out rgCore);
            OcRow(g, "Memory Clock (offset)", 118, slMem, txMem, out lblUMem, out rgMem);
            OcRow(g, "Power Limit / TDP", 170, slPow, txPow, out lblUPow, out rgPow);
            OcRow(g, "Temp Limit (alvo termico)", 222, slTemp, txTemp, out lblUTemp, out rgTemp);

            ocLock.Clear();
            ocLock.Add(UI.Lbl(g, "Travar clock do nucleo (MHz):  min", 14, 282));
            txLockC1 = UI.Txt(g, 210, 278, 50, ""); ocLock.Add(txLockC1); ocLock.Add(UI.Lbl(g, "max", 268, 282)); txLockC2 = UI.Txt(g, 292, 278, 50, ""); ocLock.Add(txLockC2);
            ocLock.Add(UI.Btn(g, "Travar", 352, 276, 70, delegate { LockClocks(true); }));
            ocLock.Add(UI.Lbl(g, "Travar clock da memoria (MHz):  min", 14, 312));
            txLockM1 = UI.Txt(g, 210, 308, 50, ""); ocLock.Add(txLockM1); ocLock.Add(UI.Lbl(g, "max", 268, 312)); txLockM2 = UI.Txt(g, 292, 308, 50, ""); ocLock.Add(txLockM2);
            ocLock.Add(UI.Btn(g, "Travar", 352, 306, 70, delegate { LockClocks(false); }));
            ocLock.Add(UI.Btn(g, "Destravar tudo", 432, 291, 110, delegate { UnlockClocks(); }));

            btnOcApply = UI.Btn(g, "Aplicar", 14, 352, 120, delegate { ApplyOc(); }); btnOcApply.Height = 28;
            btnOcReset = UI.Btn(g, "Redefinir tudo", 144, 352, 120, delegate { ResetOc(); }); btnOcReset.Height = 28;
            lblOcMsg = UI.Lbl(g, "", 14, 386, Xp.UiBold, Xp.Good);
            lblOcMsg.AutoSize = false; lblOcMsg.SetBounds(14, 386, 648, 46);
            p.Controls.Add(g);

            XpGroup c = new XpGroup(); c.Title = "Clocks atuais e suporte desta placa"; c.SetBounds(0, 446, 676, 134);
            lblClocks = UI.Lbl(c, "-", 14, 20);
            lblOcInfo = UI.Lbl(c, "", 14, 42, Xp.Ui, Xp.Dim);
            lblOcInfo.AutoSize = false; lblOcInfo.SetBounds(14, 42, 648, 62);
            UI.Lbl(c, "Crossbar / Uncore / Fabric clock: nenhuma API publica (NVML, NvAPI, ADL, IGCL) expoe esses clocks, entao nenhum programa de terceiros consegue altera-los.", 14, 100, Xp.Ui, Xp.Dim)
                .MaximumSize = new Size(648, 0);
            p.Controls.Add(c);
            return p;
        }

        void LockClocks(bool core)
        {
            if (cur == null || !cur.Nv) return;
            int mn = UI.Int(core ? txLockC1 : txLockM1, 0), mx = UI.Int(core ? txLockC2 : txLockM2, 0);
            if (mn <= 0 || mx < mn) { lblOcMsg.Text = "Informe min e max validos."; return; }
            int r = core ? Nvml.LockGpu(cur.H, (uint)mn, (uint)mx) : Nvml.LockMem(cur.H, (uint)mn, (uint)mx);
            lblOcMsg.Text = r == 0 ? "Clock travado." : "Falhou: " + Nvml.Err(r);
        }

        void UnlockClocks()
        {
            if (cur == null || !cur.Nv) return;
            int a = Nvml.UnlockGpu(cur.H), b = Nvml.UnlockMem(cur.H);
            lblOcMsg.Text = (a == 0 || b == 0) ? "Clocks destravados." : "Falhou: " + Nvml.Err(a);
        }

        // Configura um slider a partir da capacidade lida do driver
        void OcSetup(XpSlider s, XpText t, Label rg, Label unit, OcCap c, string tipText)
        {
            s.Enabled = t.Enabled = c.Ok;
            unit.Text = c.Unit.Length > 0 ? c.Unit : unit.Text;
            if (c.Ok)
            {
                s.Minimum = c.Min; s.Maximum = c.Max;
                s.Value = Math.Max(c.Min, Math.Min(c.Max, c.Cur));
                t.Text = s.Value.ToString();
                rg.Text = "(" + c.Min + Lang.Pick(" a ", " to ") + c.Max + ")";
            }
            else
            {
                s.Minimum = 0; s.Maximum = 1; s.Value = 0; t.Text = "-";
                rg.Text = "(indisponivel)";
            }
            tip.SetToolTip(s, c.Ok ? tipText : "Indisponivel: " + c.Why);
        }

        void ApplyOc()
        {
            GpuOc oc = cur == null ? null : cur.Oc;
            if (oc == null) { lblOcMsg.ForeColor = Xp.Bad; lblOcMsg.Text = "Overclock indisponivel para esta GPU. " + (cur != null ? cur.OcWhy : ""); return; }
            List<string> lines = new List<string>();
            bool bad = false;
            Action<string, string> add = delegate(string k, string r) { lines.Add(k + ": " + r); if (!GpuOc.IsOk(r) && !r.StartsWith("nao suportado")) bad = true; };
            if (oc.Core.Ok) { add("Core", oc.SetCore(slCore.Value)); }
            if (oc.Mem.Ok) { add("Mem", oc.SetMem(slMem.Value)); }
            if (oc.Power.Ok && slPow.Value != oc.Power.Cur) { string r = oc.SetPower(slPow.Value); if (GpuOc.IsOk(r)) oc.Power.Cur = slPow.Value; add("TDP", r); }
            if (oc.Temp.Ok && slTemp.Value != oc.Temp.Cur) { string r = oc.SetTemp(slTemp.Value); if (GpuOc.IsOk(r)) oc.Temp.Cur = slTemp.Value; add("Temp", r); }
            if (lines.Count == 0) lines.Add("Nenhum ajuste disponivel nesta placa.");
            lblOcMsg.ForeColor = bad ? Xp.Bad : Xp.Good;
            lblOcMsg.Text = string.Join("\n", lines.ToArray());
        }

        void ResetOc()
        {
            GpuOc oc = cur == null ? null : cur.Oc;
            if (oc == null) return;
            string r = oc.ResetAll();
            LoadOc();
            lblOcMsg.ForeColor = GpuOc.IsOk(r) ? Xp.Good : Xp.Bad;
            lblOcMsg.Text = GpuOc.IsOk(r) ? "Valores originais restaurados." : "Redefinir: " + r;
        }

        // le do driver o que a GPU atual permite e prepara a pagina de OC
        void LoadOc()
        {
            GpuOc oc = cur == null ? null : cur.Oc;
            bool any = oc != null;
            foreach (Control c in ocLock) c.Enabled = any && oc.CanLock && cur.Nv;
            btnOcApply.Enabled = btnOcReset.Enabled = any;
            if (any)
            {
                oc.Refresh();
                grpOc.Title = Lang.T("Overclock / limites - ") + oc.Backend;
                OcSetup(slCore, txCore, rgCore, lblUCore, oc.Core, "Offset do clock do nucleo, em MHz acima do padrao");
                OcSetup(slMem, txMem, rgMem, lblUMem, oc.Mem, "Offset do clock da memoria, em MHz");
                OcSetup(slPow, txPow, rgPow, lblUPow, oc.Power, "Limite de potencia (TDP)");
                OcSetup(slTemp, txTemp, rgTemp, lblUTemp, oc.Temp, "Limite de temperatura");
                List<string> info = new List<string>();
                if (!oc.Core.Ok) info.Add("Core: " + oc.Core.Why);
                else if (oc.Core.Why.Length > 0) info.Add("Core: " + oc.Core.Why);
                if (!oc.Mem.Ok) info.Add("Memoria: " + oc.Mem.Why);
                else if (oc.Mem.Why.Length > 0) info.Add("Memoria: " + oc.Mem.Why);
                if (!oc.Power.Ok) info.Add("TDP: " + oc.Power.Why);
                if (!oc.Temp.Ok) info.Add("Temperatura: " + oc.Temp.Why);
                if (oc.Note.Length > 0 && (!oc.Core.Ok || !oc.Mem.Ok)) info.Add(oc.Note);
                lblOcInfo.Text = info.Count == 0 ? "Esta placa expoe todos os ajustes acima. " + oc.Note : string.Join("\n", info.ToArray());
                lblOcMsg.Text = "";
            }
            else
            {
                grpOc.Title = Lang.T("Overclock / limites");
                XpSlider[] ss = { slCore, slMem, slPow, slTemp }; XpText[] tt = { txCore, txMem, txPow, txTemp }; Label[] rr = { rgCore, rgMem, rgPow, rgTemp };
                for (int i = 0; i < 4; i++) { ss[i].Enabled = tt[i].Enabled = false; ss[i].Minimum = 0; ss[i].Maximum = 1; ss[i].Value = 0; tt[i].Text = "-"; rr[i].Text = "(indisponivel)"; }
                string why = cur == null ? "" : cur.OcWhy;
                lblOcInfo.Text = "Overclock indisponivel para esta GPU. " + why;
                lblOcMsg.ForeColor = Xp.Bad;
                lblOcMsg.Text = "Nenhum backend de overclock para esta GPU. " + why;
            }
        }

        // ---------------- pagina de especificacoes ----------------
        Panel BuildSpecPage()
        {
            Panel p = new Panel(); p.Dock = DockStyle.Fill;
            XpGroup g = new XpGroup(); g.Title = "Especificacoes da GPU selecionada"; g.Dock = DockStyle.Fill; g.Padding = new Padding(10, 22, 10, 40);
            lvSpec.Dock = DockStyle.Fill; lvSpec.View = View.Details; lvSpec.FullRowSelect = true; lvSpec.GridLines = true;
            lvSpec.BackColor = Xp.EditBg; lvSpec.Font = Xp.Ui;
            lvSpec.Columns.Add("Propriedade", 250); lvSpec.Columns.Add("Valor", 380);
            g.Controls.Add(lvSpec);
            XpButton copy = new XpButton(); copy.Text = "Copiar tudo"; copy.SetBounds(10, 0, 110, 24);
            copy.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            g.Controls.Add(copy);
            XpButton look = new XpButton(); look.Text = "LookUp"; look.SetBounds(126, 0, 110, 24);
            look.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            g.Controls.Add(look);
            g.Resize += delegate { copy.Top = g.Height - 32; look.Top = copy.Top; };
            look.Click += delegate { if (cur == null) { Dlg.Show(this, "Nenhuma GPU selecionada.", "BunoTunner"); return; } Tpu.Open(cur.Name, true, this); };
            copy.Click += delegate
            {
                StringBuilder sb = new StringBuilder();
                foreach (ListViewItem it in lvSpec.Items) sb.AppendLine(it.Text + ": " + it.SubItems[1].Text);
                try { Clipboard.SetText(sb.ToString()); } catch (Exception) { }
            };
            p.Controls.Add(g);
            return p;
        }

        // ---------------- helpers de lista / combos ----------------
        static void AddSpec(ListView lv, string[] r)
        {
            ListViewItem it = new ListViewItem(r);
            string v = r.Length > 1 ? r[1] : "";
            if (r[0].StartsWith("---")) it.ForeColor = Xp.GroupText;
            else if (v.StartsWith("Ligado")) it.ForeColor = Xp.Good;
            else if (v.StartsWith("Desligado")) it.ForeColor = Xp.Bad;
            else if (v.StartsWith("Indisponivel")) it.ForeColor = Xp.Dim;
            lv.Items.Add(it);
        }

        ComboBox Combo(Control parent, int x, int y, int w, params string[] items)
        {
            ComboBox c = new ComboBox(); c.DropDownStyle = ComboBoxStyle.DropDownList; c.Font = Xp.Ui; c.SetBounds(x, y, w, 22);
            foreach (string s in items) c.Items.Add(s);
            if (items.Length > 0) c.SelectedIndex = 0;
            parent.Controls.Add(c);
            return c;
        }

        void StartGpuExtras()
        {
            Gpu g = cur;
            if (g == null) return;
            int gen = gpuGen;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<string[]> rows = GpuWmi.Collect(g.Name);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (gen != gpuGen) return;
                        foreach (string[] r in rows) AddSpec(lvSpec, r);
                    }));
                }
                catch (Exception) { }
            });
        }

        // ---------------- pagina de benchmark / estresse ----------------
        XpIconButton[] bBtn = new XpIconButton[5];
        ComboBox[] cbDurK = new ComboBox[5];
        ComboBox cbBThrF;
        XpButton btnBSeq;

        // um cartao por teste (grade de 2 colunas): icone que inicia/para, descricao, duracao e opcoes do teste
        XpGroup BenchBlock(int k, string title, string desc, string icon, string tipText)
        {
            XpGroup g = new XpGroup(); g.Title = title; g.SetBounds((k % 2) * 342, (k / 2) * 192, 334, 184);
            XpIconButton b = new XpIconButton(icon, 48); b.Location = new Point(12, 30); b.Badge = 1;
            int kk = k;
            b.Click += delegate { ToggleBench(kk); };
            tip.SetToolTip(b, tipText);
            g.Controls.Add(b); bBtn[k] = b;
            Label d = UI.Lbl(g, desc, 72, 28, Xp.Ui, Xp.Dim);
            d.AutoSize = false; d.SetBounds(72, 28, 252, 58);
            UI.Lbl(g, "Duracao:", 12, 96);
            cbDurK[k] = Combo(g, 76, 92, 110, "30 s", "1 min", "5 min", "10 min", "Ate parar"); cbDurK[k].SelectedIndex = 1;
            return g;
        }

        Panel BuildBenchPage()
        {
            Panel p = new Panel(); p.Dock = DockStyle.Fill;

            XpGroup gG = BenchBlock(0, "GPU  -  render 3D (estilo FurMark)", "Abre uma janela OpenGL com shader e pelos e mostra o FPS. Fechar a janela para o teste.", "gpu", "Iniciar / parar o estresse de GPU");
            UI.Lbl(gG, "Placa:", 12, 128);
            lblBGpu = UI.Lbl(gG, cur != null ? cur.Name : "-", 56, 128, Xp.UiBold, Xp.GroupText);
            lblBGpu.AutoSize = false; lblBGpu.SetBounds(56, 128, 268, 16);
            Label gn = UI.Lbl(gG, "A placa e a escolhida la em cima. Se o OpenGL abrir em outra, o teste muda para OpenCL na escolhida.", 12, 150, Xp.Ui, Xp.Dim);
            gn.AutoSize = false; gn.SetBounds(12, 150, 312, 30);
            p.Controls.Add(gG);

            XpGroup gC = BenchBlock(1, "CPU  -  ray tracing 3D (estilo Cinebench)", "Renderiza uma cena em todos os nucleos, bloco a bloco. Pontos = Mrays/s x 100.", "chip", "Iniciar / parar o estresse de CPU");
            UI.Lbl(gC, "Threads:", 12, 128);
            cbBThr = Combo(gC, 76, 124, 110, "Todas", "Metade", "1");
            p.Controls.Add(gC);

            XpGroup gF = BenchBlock(2, "FPU  -  campo de ondas em ponto flutuante", "Calculo de ondas com seno/cosseno em todos os nucleos; mostra GFLOPS estimado.", "fpu", "Iniciar / parar o estresse de FPU");
            UI.Lbl(gF, "Threads:", 12, 128);
            cbBThrF = Combo(gF, 76, 124, 110, "Todas", "Metade", "1");
            cbBThr.SelectedIndexChanged += delegate { if (cbBThrF.SelectedIndex != cbBThr.SelectedIndex) cbBThrF.SelectedIndex = cbBThr.SelectedIndex; };
            cbBThrF.SelectedIndexChanged += delegate { if (cbBThr.SelectedIndex != cbBThrF.SelectedIndex) cbBThr.SelectedIndex = cbBThrF.SelectedIndex; };
            p.Controls.Add(gF);

            XpGroup gR = BenchBlock(3, "RAM  -  escrita e leitura com verificacao", "Grava e le padroes na memoria e confere cada valor; qualquer diferenca conta como erro.", "ram", "Iniciar / parar o estresse de RAM");
            UI.Lbl(gR, "Usar (% da livre):", 12, 128);
            cbBRam = Combo(gR, 124, 124, 70, "25%", "50%", "70%"); cbBRam.SelectedIndex = 1;
            p.Controls.Add(gR);

            XpGroup gS = BenchBlock(4, "SSD  -  sequencial + 4K aleatorio", "Usa um arquivo temporario no drive escolhido (apagado ao fim). Em 'Ate parar' gera desgaste.", "ssd", "Iniciar / parar o estresse de SSD");
            UI.Lbl(gS, "Drive:", 12, 128);
            cbBDrive = Combo(gS, 56, 124, 70);
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try { if (d.DriveType == DriveType.Fixed && d.IsReady) cbBDrive.Items.Add(d.Name); } catch (Exception) { }
            }
            if (cbBDrive.Items.Count > 0)
            {
                int si = cbBDrive.Items.IndexOf(Path.GetPathRoot(Environment.SystemDirectory));
                cbBDrive.SelectedIndex = si >= 0 ? si : 0;
            }
            UI.Lbl(gS, "Tamanho:", 146, 128);
            cbBSize = Combo(gS, 206, 124, 90, "512 MB", "1 GB", "2 GB", "4 GB"); cbBSize.SelectedIndex = 1;
            p.Controls.Add(gS);

            // cartao de controle: seguranca, progresso, sequencia e mensagens
            XpGroup gK = new XpGroup(); gK.Title = "Controle e seguranca"; gK.SetBounds(342, 384, 334, 184);
            chkBSafe = UI.Opt(gK, "Parar se passar de", 12, 28, 150, false); chkBSafe.Checked = true;
            txBTemp = UI.Txt(gK, 168, 25, 40, "95");
            UI.Lbl(gK, "\u00B0C", 212, 29);
            pbBench = new XpProgress(); pbBench.SetBounds(12, 60, 310, 18);
            gK.Controls.Add(pbBench);
            btnBSeq = UI.Btn(gK, "Rodar sequencia (CPU, FPU, RAM, GPU)", 12, 88, 310, delegate { ToggleSeq(); });
            btnBSeq.Height = 28;
            tip.SetToolTip(btnBSeq, "Roda CPU, FPU, RAM e GPU um apos o outro, cada um com a duracao do seu bloco (o SSD fica de fora para nao gerar desgaste).");
            p.Controls.Add(gK);
            BenchRestore();

            XpGroup g3 = new XpGroup(); g3.Title = "Resultado ao vivo"; g3.SetBounds(0, 380, 676, 204);
            UI.Lbl(g3, "Taxa atual", 14, 22, Xp.UiBold);
            lblBRate = UI.Lbl(g3, "-", 14, 36, Xp.Big, Xp.GroupText);
            string[] caps = { "Media:", "Pico:", "Temperatura:", "Erros:", "Tempo:" };
            Label[] vals = new Label[5];
            for (int i = 0; i < 5; i++)
            {
                UI.Lbl(g3, caps[i], 14, 68 + i * 15, Xp.UiBold);
                vals[i] = UI.Lbl(g3, "-", 100, 68 + i * 15);
            }
            lblBAvg = vals[0]; lblBPeak = vals[1]; lblBTemp = vals[2]; lblBErr = vals[3]; lblBTime = vals[4];
            lblBMsg = UI.Lbl(g3, "Clique no icone de um teste para iniciar.", 14, 146, Xp.Ui, Xp.Dim);
            lblBMsg.AutoSize = false; lblBMsg.SetBounds(14, 146, 232, 54);
            grBench.SetBounds(252, 22, 412, 100); grBench.Window = 120;
            g3.Controls.Add(grBench);
            lvBench.SetBounds(252, 126, 412, 50); lvBench.View = View.Details; lvBench.FullRowSelect = true; lvBench.GridLines = true;
            lvBench.BackColor = Xp.EditBg; lvBench.Font = Xp.Ui;
            lvBench.Columns.Add("Teste", 38); lvBench.Columns.Add("Dur.", 38); lvBench.Columns.Add("Media", 82); lvBench.Columns.Add("Pico", 82);
            lvBench.Columns.Add("Erros", 36); lvBench.Columns.Add("T.max", 40); lvBench.Columns.Add("Estab.", 46); lvBench.Columns.Add("vs ant.", 50);
            g3.Controls.Add(lvBench);
            XpButton bCopy = UI.Btn(g3, "Copiar", 252, 178, 72, delegate { BenchCopy(); });
            XpButton bCsv = UI.Btn(g3, "Salvar CSV", 330, 178, 84, delegate { BenchCsv(); });
            XpButton bClr = UI.Btn(g3, "Limpar", 420, 178, 64, delegate { lvBench.Items.Clear(); });
            bCopy.Height = bCsv.Height = bClr.Height = 22;
            // "Resultado ao vivo" fica fora da tela (os controles continuam existindo); so a linha de status aparece, no cartao de controle
            lblBMsg.Parent = gK; lblBMsg.SetBounds(12, 122, 310, 58);

            tmBench.Interval = 1000;
            tmBench.Tick += delegate { try { BenchTick(); } catch (Exception) { } };
            return p;
        }

        void ToggleSeq()
        {
            if (bRunning) { StopBench("Interrompido pelo usuario."); return; }
            bQueue = new Queue<int>(new int[] { 1, 2, 3, 0 }); bSeqRun = true;
            ToggleBench(-1);
        }

        bool bGlChecked; Label lblBGpu;
        double bSq; Queue<int> bQueue = new Queue<int>(); bool bSeqRun;
        void BenchRestore()
        {
            try
            {
                string st = Settings.Get("bench", "");
                string[] a = st.Split(',');
                if (a.Length < 7) return;
                int v;
                if (int.TryParse(a[1], out v) && v >= 0 && v < 5) foreach (ComboBox cd in cbDurK) cd.SelectedIndex = v;
                if (int.TryParse(a[2], out v) && v >= 0 && v < cbBThr.Items.Count) cbBThr.SelectedIndex = v;
                if (int.TryParse(a[3], out v) && v >= 0 && v < cbBRam.Items.Count) cbBRam.SelectedIndex = v;
                if (int.TryParse(a[4], out v) && v >= 0 && v < cbBSize.Items.Count) cbBSize.SelectedIndex = v;
                chkBSafe.Checked = a[5] == "1";
                if (a[6].Length > 0 && a[6].Length < 4) txBTemp.Box.Text = a[6];
            }
            catch (Exception) { }
        }
        void BenchSave(int kind)
        {
            try
            {
                Settings.Set("bench", string.Join(",", new string[] { kind.ToString(), cbDurK[kind].SelectedIndex.ToString(), cbBThr.SelectedIndex.ToString(), cbBRam.SelectedIndex.ToString(), cbBSize.SelectedIndex.ToString(), chkBSafe.Checked ? "1" : "0", txBTemp.Box.Text }));
            }
            catch (Exception) { }
        }
        string BenchText(string sep)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Join(sep, new string[] { "Teste", "Duracao", "Media", "Pico", "Erros", "Temp max", "Estabilidade", "Variacao" }));
            for (int i = lvBench.Items.Count - 1; i >= 0; i--)
            {
                List<string> c = new List<string>();
                foreach (ListViewItem.ListViewSubItem x in lvBench.Items[i].SubItems) c.Add(x.Text);
                sb.AppendLine(string.Join(sep, c.ToArray()));
            }
            return sb.ToString();
        }
        void BenchCopy()
        {
            if (lvBench.Items.Count == 0) { lblBMsg.Text = "Nenhum resultado para copiar ainda."; return; }
            try { Clipboard.SetText(BenchText("\t")); lblBMsg.Text = "Resultados copiados."; } catch (Exception) { }
        }
        void BenchCsv()
        {
            if (lvBench.Items.Count == 0) { lblBMsg.Text = "Nenhum resultado para salvar ainda."; return; }
            using (SaveFileDialog sd = new SaveFileDialog())
            {
                sd.Filter = "CSV|*.csv"; sd.FileName = "benchmark_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv";
                if (sd.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(sd.FileName, BenchText(";"), new UTF8Encoding(true)); lblBMsg.Text = "Salvo em " + sd.FileName; }
                catch (Exception ex) { lblBMsg.Text = "Falha ao salvar: " + ex.Message; }
            }
        }

        void BenchLock(bool run)
        {
            for (int i = 0; i < 5; i++) { bBtn[i].Enabled = !run || i == bKind; cbDurK[i].Enabled = !run; }
            cbBThrF.Enabled = !run; cbBThr.Enabled = !run; cbBRam.Enabled = !run; cbBDrive.Enabled = !run; cbBSize.Enabled = !run;
        }

        void ToggleBench(int pick)
        {
            if (bRunning) { StopBench("Interrompido pelo usuario."); return; }
            if (pick < 0)
            {
                if (!bSeqRun || bQueue.Count == 0) { bSeqRun = false; return; }
                pick = bQueue.Dequeue();
            }
            else bSeqRun = false;
            if (Bench.Alive) { lblBMsg.Text = "Finalizando o teste anterior... tente de novo em um instante."; return; }
            int kind = pick;
            int[] durs = { 30, 60, 300, 600, 0 };
            int di = cbDurK[kind].SelectedIndex; if (bSeqRun && di == 4) di = 1;
            bDurSec = durs[di];
            int cores = Environment.ProcessorCount;
            int thr = cbBThr.SelectedIndex == 0 ? cores : (cbBThr.SelectedIndex == 1 ? Math.Max(1, cores / 2) : 1);   // CPU e FPU compartilham
            int ramPct = new int[] { 25, 50, 70 }[cbBRam.SelectedIndex];
            int mb = new int[] { 512, 1024, 2048, 4096 }[cbBSize.SelectedIndex];
            string drive = cbBDrive.SelectedItem as string;
            if (kind == 4)
            {
                if (drive == null) { lblBMsg.Text = "Nenhum drive disponivel para o teste de SSD."; return; }
                if (bDurSec == 0 && Dlg.Show("Em 'Ate parar' o SSD recebe gravacoes continuas (gera desgaste). Continuar?", "BunoTunner", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            BenchSave(kind); bGlChecked = false;
            string gname = cur != null ? cur.Name : null;
            bExt = kind <= 2; bU = BUnit[kind]; bSc = BScale[kind];
            if (kind == 0) { bU = "FPS"; bSc = 1.0; }
            bFallback = delegate { Bench.Start(kind, thr, ramPct, drive, mb, gname); };
            if (bExt)
            {
                Bench.Stop = false; Th.Interlocked.Exchange(ref Bench.Counter, 0); Th.Interlocked.Exchange(ref Bench.Errors, 0); Bench.Extra = ""; Bench.Fail = null;
            }
            else Bench.Start(kind, thr, ramPct, drive, mb, gname);
            bKind = kind; bRunning = true; bStart = DateTime.UtcNow; bLastT = bStart; bLast = 0;
            bSum = 0; bSq = 0; bPeak = 0; bTempMax = 0; bN = 0; bRateNow = null; bTempNow = null;

            bsnRate = new Sn(); bsnRate.Group = "Benchmark"; bsnRate.Name = "Taxa (" + bU + ")"; bsnRate.Type = "Data"; bsnRate.Get = delegate { return bRateNow; };
            bsnTemp = new Sn(); bsnTemp.Group = "Benchmark"; bsnTemp.Name = "Temperatura"; bsnTemp.Type = "Temperature"; bsnTemp.Get = delegate { return bTempNow; };
            grBench.Items.Clear();
            Series s1 = new Series(); s1.Sn = bsnRate; s1.Color = Color.FromArgb(0, 200, 0);
            Series s2 = new Series(); s2.Sn = bsnTemp; s2.Color = Color.FromArgb(255, 140, 0);
            grBench.Items.Add(s1); grBench.Items.Add(s2);

            BenchLock(true);
            try
            {
                if (rf != null && !rf.IsDisposed) rf.Close();
                rf = new RenderForm(kind, thr);
                rf.Cv.Unit = bU; rf.Cv.Total = bDurSec;
                rf.FormClosed += delegate { rf = null; if (bRunning) StopBench("Janela de render fechada: teste interrompido."); };
                rf.Show(this);
            }
            catch { rf = null; }
            bBtn[kind].Badge = 2; bBtn[kind].Invalidate(); btnBSeq.Text = "Parar tudo";
            pbBench.Marquee = bDurSec == 0; pbBench.Value = 0;
            lblBMsg.Text = "Rodando...";
            tmBench.Start();
        }

        float? BenchTemp(int kind)
        {
            if (kind == 0)
            {
                int? t = cur == null ? null : cur.Temp();
                return t.HasValue ? (float?)t.Value : null;
            }
            string pre = kind == 4 ? "Armazenamento" : "Processador";
            string[] names = kind == 4 ? new string[] { "" } : new string[] { "Package", "Tdie", "Tctl", "Core Max", "Core", "" };
            foreach (string nm in names)
                foreach (Sn s in SensorHub.PcSensors)
                    if (s.Group.StartsWith(pre) && s.Type == "Temperature" && s.IsRealTemp && s.Last.HasValue && s.Name.IndexOf(nm, StringComparison.OrdinalIgnoreCase) >= 0)
                        return s.Last;
            return null;
        }

        void BenchTick()
        {
            if (!bRunning) return;
            if (bExt && bKind == 0 && !bGlChecked && rf != null && rf.Gl != null && rf.Gl.State == 1 && rf.Gl.Renderer.Length > 0)
            {
                bGlChecked = true;
                string want = cur != null ? cur.Name : null;
                if (want != null && cmb.Items.Count > 1 && !Bench.GpuNameMatch(rf.Gl.Renderer, want))
                {
                    // o Windows escolhe a placa do OpenGL; para respeitar a placa selecionada, usa o OpenCL nela
                    string got = rf.Gl.Renderer;
                    rf.Gl.StopRender();
                    bExt = false; bU = BUnit[0]; bSc = BScale[0]; bLast = 0; bLastT = DateTime.UtcNow;
                    if (rf.Cv != null) rf.Cv.Unit = bU;
                    lblBMsg.Text = "O OpenGL abriu na placa '" + got + "', diferente da selecionada ('" + want + "'): usando o teste OpenCL na placa selecionada.";
                    bFallback();
                    return;
                }
            }
            if (bExt && bKind == 0 && rf != null && rf.Gl != null && rf.Gl.State == 2)
            {
                bExt = false; bU = BUnit[0]; bSc = BScale[0]; bLast = 0; bLastT = DateTime.UtcNow;
                if (rf.Cv != null) rf.Cv.Unit = bU;
                lblBMsg.Text = "OpenGL indisponivel (" + rf.Gl.Error + "): usando o teste OpenCL.";
                bFallback();
                return;
            }
            DateTime now = DateTime.UtcNow;
            double dt = (now - bLastT).TotalSeconds;
            if (dt < 0.5) return;
            double elapsed = (now - bStart).TotalSeconds;
            long c = Th.Interlocked.Read(ref Bench.Counter);
            double rate = (c - bLast) / dt * bSc;
            bLast = c; bLastT = now;
            float? t = BenchTemp(bKind);
            bRateNow = (float)rate; bTempNow = t;
            bsnRate.Poll(); bsnTemp.Poll();
            foreach (Series se in grBench.Items)
            {
                if (!se.Sn.Last.HasValue) continue;
                se.V.Add(se.Sn.Last.Value);
                if (se.V.Count > XpGraph.Store) se.V.RemoveAt(0);
            }
            grBench.Invalidate();
            if (elapsed > 1.5) { bSum += rate; bSq += rate * rate; bN++; if (rate > bPeak) bPeak = rate; }

            string u = bU;
            long errs = Th.Interlocked.Read(ref Bench.Errors);
            if (rf != null && !rf.IsDisposed)
            {
                rf.Text = "BunoTunner - " + BName[bKind] + ": " + rate.ToString("0.0") + " " + bU + " (Esc fecha e para)"; rf.Invalidate(new Rectangle(0, 0, rf.Width, 32));
                rf.Cv.Rate = rate; rf.Cv.Temp = t.HasValue ? t.Value : double.NaN; rf.Cv.Elapsed = elapsed; rf.Cv.Errors = errs; rf.Cv.Extra = Bench.Extra;
            }
            lblBRate.Text = rate.ToString("0.0") + " " + u;
            lblBAvg.Text = bN > 0 ? (bSum / bN).ToString("0.0") + " " + u : "-";
            lblBPeak.Text = bPeak.ToString("0.0") + " " + u;
            if (t.HasValue) { bTempMax = Math.Max(bTempMax, t.Value); lblBTemp.Text = t.Value.ToString("0") + " \u00B0C  (max " + bTempMax.ToString("0") + ")"; }
            else lblBTemp.Text = "-";
            lblBErr.Text = errs.ToString(); lblBErr.ForeColor = errs > 0 ? Xp.Bad : Xp.Text;
            int es = (int)elapsed;
            lblBTime.Text = (es / 60).ToString("00") + ":" + (es % 60).ToString("00") + (bDurSec > 0 ? " / " + (bDurSec / 60).ToString("00") + ":" + (bDurSec % 60).ToString("00") + "   (restam " + Math.Max(0, bDurSec - es) + " s)" : "");
            string ex = Bench.Extra;
            if (!string.IsNullOrEmpty(ex)) lblBMsg.Text = ex;
            if (bDurSec > 0) pbBench.Value = (int)(100 * elapsed / bDurSec); else pbBench.Step();

            if (Bench.Fail != null) { StopBench("Falhou: " + Bench.Fail); return; }
            if (chkBSafe.Checked && t.HasValue && t.Value >= UI.Int(txBTemp, 95)) { StopBench("Parado por seguranca: temperatura de " + t.Value.ToString("0") + " \u00B0C."); return; }
            if (bDurSec > 0 && elapsed >= bDurSec) { StopBench("Concluido."); return; }
            if (elapsed > 3 && !bExt && !Bench.Alive) { StopBench("Teste finalizado."); return; }
        }

        string BenchDelta()
        {
            if (bN < 1) return "-";
            double avg = bSum / bN;
            foreach (ListViewItem x in lvBench.Items)
                if (x.SubItems[0].Text == BName[bKind] && x.Tag is double)
                {
                    double pv = (double)x.Tag;
                    if (pv > 0) { double d = (avg - pv) / pv * 100; return (d >= 0 ? "+" : "") + d.ToString("0.0") + "%"; }
                }
            return "-";
        }

        string BenchStab()
        {
            if (bN < 3) return "-";
            double m = bSum / bN, vr2 = bSq / bN - m * m;
            if (m <= 0) return "-";
            double sd = Math.Sqrt(Math.Max(0, vr2));
            return Math.Max(0, 100 * (1 - sd / m)).ToString("0") + "%";
        }

        void StopBench(string msg)
        {
            bool wasRun = bRunning;
            bool okEnd = msg.StartsWith("Conclu");
            if (!okEnd && bSeqRun) { bSeqRun = false; bQueue.Clear(); msg += " (sequencia cancelada)"; }
            Bench.Stop = true; bRunning = false; tmBench.Stop();
            try { if (rf != null && !rf.IsDisposed) rf.Close(); } catch { }
            rf = null;
            BenchLock(false);
            for (int bi = 0; bi < 5; bi++) { bBtn[bi].Badge = 1; bBtn[bi].Invalidate(); }
            btnBSeq.Text = bSeqRun && okEnd && bQueue.Count > 0 ? "Parar tudo" : "Rodar sequencia (CPU, FPU, RAM, GPU)";
            pbBench.Marquee = false;
            double el = (DateTime.UtcNow - bStart).TotalSeconds;
            long errs = Th.Interlocked.Read(ref Bench.Errors);
            string u = bU;
            ListViewItem it = new ListViewItem(new string[] {
                BName[bKind], ((int)el) + " s",
                bN > 0 ? (bSum / bN).ToString("0.0") + " " + u : "-", bPeak.ToString("0.0") + " " + u,
                errs.ToString(), bTempMax > 0 ? bTempMax.ToString("0") : "-", BenchStab(), BenchDelta() });
            if (errs > 0) it.ForeColor = Xp.Bad;
            it.Tag = bN > 0 ? bSum / bN : 0.0;
            lvBench.Items.Insert(0, it);
            if (wasRun && msg.StartsWith("Conclu")) { try { System.Media.SystemSounds.Asterisk.Play(); } catch (Exception) { } }
            if (bKind == 1 && bN > 0) msg += "  Pontos CPU: " + (bSum / bN * 100).ToString("0") + " pts (" + (cbBThr.SelectedIndex == 2 ? "1 thread" : "multi") + ").";
            if (okEnd && bSeqRun)
            {
                if (bQueue.Count > 0)
                {
                    Timer nt = new Timer(); nt.Interval = 2500;
                    nt.Tick += delegate { nt.Stop(); nt.Dispose(); if (bSeqRun && !bRunning) ToggleBench(-1); };
                    nt.Start(); msg += "  Proximo teste em instantes...";
                }
                else { bSeqRun = false; msg += "  Sequencia concluida."; }
            }
            lblBMsg.Text = msg + (errs > 0 ? "  ATENCAO: " + errs + " erro(s) de calculo/verificacao - sistema instavel!" : "");
        }

        // ---------------- pagina de especificacoes da CPU ----------------
        Panel BuildCpuPage()
        {
            Panel p = new Panel(); p.Dock = DockStyle.Fill;
            XpGroup g = new XpGroup(); g.Title = "Especificacoes da CPU"; g.Dock = DockStyle.Fill; g.Padding = new Padding(10, 22, 10, 40);
            lvCpu.Dock = DockStyle.Fill; lvCpu.View = View.Details; lvCpu.FullRowSelect = true; lvCpu.GridLines = true;
            lvCpu.BackColor = Xp.EditBg; lvCpu.Font = Xp.Ui;
            lvCpu.Columns.Add("Propriedade", 250); lvCpu.Columns.Add("Valor", 380);
            lvCpu.Items.Add(new ListViewItem(new string[] { "Lendo informacoes da CPU...", "" }));
            g.Controls.Add(lvCpu);
            XpButton copy = new XpButton(); copy.Text = "Copiar tudo"; copy.SetBounds(10, 0, 110, 24);
            copy.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            g.Controls.Add(copy);
            XpButton look = new XpButton(); look.Text = "LookUp"; look.SetBounds(126, 0, 110, 24);
            look.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            g.Controls.Add(look);
            g.Resize += delegate { copy.Top = g.Height - 32; look.Top = copy.Top; };
            look.Click += delegate { Tpu.Open(CpuNameStr(), false, this); };
            copy.Click += delegate
            {
                StringBuilder sb = new StringBuilder();
                foreach (ListViewItem it in lvCpu.Items) sb.AppendLine(it.Text + ": " + it.SubItems[1].Text);
                try { Clipboard.SetText(sb.ToString()); } catch (Exception) { }
            };
            p.Controls.Add(g);
            return p;
        }

        static string CpuNameStr()
        {
            try { using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")) return Convert.ToString(k.GetValue("ProcessorNameString")).Trim(); }
            catch (Exception) { return ""; }
        }

        void FillCpu(List<string[]> rows)
        {
            lvCpu.BeginUpdate();
            lvCpu.Items.Clear(); cpuLive.Clear();
            foreach (string[] r in rows) AddSpec(lvCpu, r);
            ListViewItem sep = new ListViewItem(new string[] { "--- Ao vivo ---", "" });
            sep.ForeColor = Xp.GroupText;
            lvCpu.Items.Add(sep);
            string[] live = { "Uso total", "Temperatura", "Consumo do pacote", "Clock medio dos nucleos", "Leitura dos sensores da CPU" };
            foreach (string n in live)
            {
                ListViewItem it = new ListViewItem(new string[] { n, "-" });
                lvCpu.Items.Add(it); cpuLive.Add(it);
            }
            lvCpu.EndUpdate();
        }

        string CpuVal(string type, params string[] names)
        {
            foreach (string nm in names)
                foreach (Sn s in SensorHub.PcSensors)
                    if (s.Group.StartsWith("Processador") && s.Type == type && (type != "Temperature" || s.IsRealTemp) && s.Last.HasValue && s.Name.IndexOf(nm, StringComparison.OrdinalIgnoreCase) >= 0)
                        return s.Fmt(s.Last);
            return "-";
        }

        string CpuDiag()
        {
            bool drv = false, acpi = false;
            foreach (Sn s in SensorHub.PcSensors)
                if (s.Group.StartsWith("Processador") && s.Type == "Temperature" && s.Seen)
                {
                    if (s.Group.Contains("Zona termica")) acpi = true; else drv = true;
                }
            if (drv) return "Ok - driver de sensores funcionando";
            if (acpi) return "Driver SEM leitura da CPU (Integridade de memoria/Defender?) - usando zona termica do Windows";
            return "Sem leitura de temperatura - veja o LEIA-ME (driver bloqueado?)";
        }

        void UpdateCpuLive()
        {
            if (cpuLive.Count != 5) return;
            cpuLive[4].SubItems[1].Text = CpuDiag();
            cpuLive[0].SubItems[1].Text = CpuVal("Load", "CPU Total");
            cpuLive[1].SubItems[1].Text = CpuVal("Temperature", "Package", "Tdie", "Tctl", "Core Max", "Core");
            cpuLive[2].SubItems[1].Text = CpuVal("Power", "Package");
            float sum = 0; int cnt = 0;
            foreach (Sn s in SensorHub.PcSensors)
                if (s.Group.StartsWith("Processador") && s.Type == "Clock" && s.Name.StartsWith("Core #") && s.Last.HasValue) { sum += s.Last.Value; cnt++; }
            cpuLive[3].SubItems[1].Text = cnt > 0 ? (sum / cnt).ToString("0") + " MHz" : "-";
        }

        // ---------------- troca de GPU ----------------
        void LoadGpu()
        {
            cur = cmb.SelectedItem as Gpu;
            if (cur == null) return;
            if (lblBGpu != null) lblBGpu.Text = cur.Name;
            fanPage.SetGpu(cur);

            LoadOc();
            lvSpec.Items.Clear();
            gpuGen++;
            foreach (string[] row in cur.Specs()) AddSpec(lvSpec, row);
            if (IsHandleCreated) StartGpuExtras();
        }

        // ---------------- loop de 1 segundo ----------------
        void Tick()
        {
            string at = SensorHub.AlertText;
            if (at != null) { if (status.Text != at) status.Text = at; status.ForeColor = Xp.Bad; }
            else { if (status.Text != statusText) status.Text = statusText; status.ForeColor = Xp.Text; }
            if (pgCpu.Visible) UpdateCpuLive();
            fanPage.Tick(pgFan.Visible);
            Gpu g = cur; if (g == null) return;
            if (pgOc.Visible)
            {
                float? clk = g.Clk(0, "Core"), sm = g.Clk(1, "Shader"), mem = g.Clk(2, "Memory"), vid = g.Clk(3, "Video");
                lblClocks.Text = "Nucleo: " + (clk.HasValue ? clk.Value.ToString("0") : "-") + " MHz     SM: " + (sm.HasValue ? sm.Value.ToString("0") : "-") +
                    " MHz     Memoria: " + (mem.HasValue ? mem.Value.ToString("0") : "-") + " MHz     Video: " + (vid.HasValue ? vid.Value.ToString("0") : "-") + " MHz";
            }
        }

        // ---------------- bandeja do sistema ----------------
        NotifyIcon tray; bool trayTold;
        void InitTray()
        {
            try
            {
                tray = new NotifyIcon();
                tray.Icon = Icon; tray.Text = "BunoTunner";
                ContextMenuStrip cm = new ContextMenuStrip();
                cm.Items.Add("Abrir BunoTunner", null, delegate { ShowFromTray(); });
                ToolStripMenuItem mi = new ToolStripMenuItem("Botao - (minimizar) envia para a bandeja");
                mi.CheckOnClick = true; mi.Checked = Settings.Get("tray", "1") == "1";
                mi.CheckedChanged += delegate { Settings.Set("tray", mi.Checked ? "1" : "0"); };
                cm.Items.Add(mi);
                cm.Items.Add(new ToolStripSeparator());
                cm.Items.Add("Sair", null, delegate { Close(); });
                Lang.Menu(cm);
                tray.ContextMenuStrip = cm;
                tray.DoubleClick += delegate { ShowFromTray(); };
                tray.Visible = true;
            }
            catch (Exception) { tray = null; }
        }
        // botao "-" da janela: vai para a bandeja (se a opcao estiver ligada); o X fecha o programa
        protected override void OnMinimizeClick()
        {
            if (tray == null || Settings.Get("tray", "1") != "1") { base.OnMinimizeClick(); return; }
            Hide();
            if (!trayTold)
            {
                trayTold = true;
                try { tray.ShowBalloonTip(2500, "BunoTunner", Lang.T("Continua rodando na bandeja. Clique duas vezes no icone para abrir."), ToolTipIcon.Info); } catch (Exception) { }
            }
        }
        void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void OnClosing(object s, FormClosingEventArgs e)
        {
            if (tray != null) { try { tray.Visible = false; tray.Dispose(); } catch (Exception) { } tray = null; }
            Bench.Stop = true;
            timer.Stop();
            try { fanPage.RestoreAll(); affPage.Shutdown(); } catch (Exception) { }
            if (sensForm != null && !sensForm.IsDisposed) sensForm.Close();
            SensorHub.Close();
        }
    }
}
