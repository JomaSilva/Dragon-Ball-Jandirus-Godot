using Jandirus.Core.Forms;

namespace Jandirus.Core.Tech;

/// <summary>
/// AS QUATRO FERRAMENTAS DE PAREDE -- `Wall_Repair`, `Wall_Upgrader`, `Portable_Repairer` e `Destructor`
/// (`Code/Modules/Turfs/Building/buildable.dm:41-176`).
///
/// ============================ O QUE O ORIGINAL FAZ ============================
///   * `Wall_Repair`: a cada minuto (`sleep(600)`) cura as barreiras a 15 tiles (`range(15, src)`).
///   * `Wall_Upgrader`: cura igual, e ao ser clicado leva a armadura maxima de tudo o que ha a 15 tiles ao teto de
///     tecnologia de quem clicou (`M.maxarmor = usr.intBPcap`), e a resistencia das paredes tambem
///     (`if(T.Resistance&lt;usr.intBPcap) T.Resistance=usr.intBPcap`).
///   * `Portable_Repairer`: clicado, cura o que ha a um tile.
///   * `Destructor`: por 5.000 zeni, destroi o que ha a um tile -- os objetos de armadura ate o teto de tecnologia
///     de quem paga (ou dele), e os tiles de resistencia ate esse teto (ou dele).
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * AS TRES CURAS DO ORIGINAL CURAM A PROPRIA FERRAMENTA, e nao o que esta em volta: o laco e
///     `for(var/obj/M in range(15,src)) if(M.fragile) healDamage(maxarmor)` -- falta o `M.` na frente do
///     `healDamage`. O que se porta e o que a descricao promete (*"Automatically heals all barriers in its range"*).
///   * O REFORCO SO SOBE. La o `M.maxarmor = usr.intBPcap` e incondicional: um novato clicando rebaixaria a
///     armadura da maquina de um veterano. Aqui a armadura maxima so muda quando o teto de quem clica e maior.
///   * AS "BARREIRAS" DESTE PORT SAO AS CONSTRUCOES (o que tem armadura -- `Obra.Armadura`), e as "paredes" sao os
///     blocos erguidos no modo de construir (o que tem resistencia -- `BlocoDePe.Resistencia`). O cenario do mapa
///     nao tem dono nem vida: so o destruidor mexe nele, pelo limiar de sempre.
///   * O DESTRUIDOR ACEITA RECURSOS, pelo dobro (dono, 2026-10-10).
/// ==================================================================================
/// </summary>
public static class MurosDeTech
{
	public const string Reparador = "Wall_Repair", Reforcador = "Wall_Upgrader",
						ReparadorPortatil = "Portable_Repairer", Destruidor = "Destructor";

	public const string VerboReforcar = "muro_reforcar", VerboInfo = "muro_info";
	public const string AcaoReparar = "muro_reparar", AcaoDestruir = "muro_destruir";

	/// <summary>`range(15, src)`: ate onde o reparador e o reforcador alcancam, em tiles.</summary>
	public const int RaioEmTiles = 15;

	/// <summary>`sleep(600)`: de quanto em quanto os dois curam o que ha em volta.</summary>
	public const double SegundosEntreReparos = 60;

	/// <summary>`range(1, src)` / `view(1, usr)`: o alcance das duas ferramentas de mao, em tiles.</summary>
	public const int RaioDeMaoEmTiles = 1;

	/// <summary>`usr.zenni -= 5000`: o que cada uso do destruidor custa.</summary>
	public const double CustoDeDestruir = 5_000;

	/// <summary>Esta obra cura o que ha em volta sozinha?</summary>
	public static bool CuraSozinha(string tipo) => tipo is Reparador or Reforcador;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X3): o reparador nao repara nada -- a antena parada da v0.2.0.</summary>
	public static bool NaoReparaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X3): o destruidor derruba o que e mais forte que a tecnologia de quem o usa.</summary>
	public static bool DestroiTudoDeTeste;
}

/// <summary>
/// A ESTACAO DE RECARGA -- `obj/items/Recharge_Station` (`Code/Modules/Tech/Androids.dm:568-675`).
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// Aparafusada, a cada volta do relogio dela (um segundo e pouco) olha quem esta EM CIMA dela (`view(0)`), e so o
/// primeiro: se e um androide, devolve `efficiency` de folego e `efficiency * MaxKi / 1000` de Ki, e cada uma das
/// duas coisas custa `5 / efficiency` da energia da estacao. A energia comeca em 100 e so volta com as melhorias:
/// os PAINEIS SOLARES rendem `tier` por volta, e do nivel 2 em diante a capacidade e `100 * tier**2` e a estacao
/// rende mais `tier` por volta. As melhorias: eficiencia (+0,5 por `20000 * efficiency`, ate 4), nivel
/// (`10000 * tier`, ate 6) e os paineis (100.000).
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * A ENERGIA TEM TETO. La a soma nao confere a capacidade, e a estacao com paineis enche sem parar.
///   * UMA VOLTA POR SEGUNDO (la, os `sleep` somam entre 1,1 e 1,3 s).
///   * OS MODULOS DE CIBORGUE que ela tambem recarrega no original nao existem neste port.
///   * AS MELHORIAS ACEITAM RECURSOS, pelo dobro.
/// ==================================================================================
/// </summary>
public static class EstacaoDeRecarga
{
	public const string Tipo = "Recharge_Station";

	public const string VerboInfo = "recarga_info", VerboMelhorar = "recarga_up";

	/// <summary>As tres melhorias, como viajam no argumento do verbo.</summary>
	public const string Eficiencia = "eficiencia", Nivel = "nivel", Solar = "solar";

	// ---- as chaves do estado (`Obra.Tech.N`) ----
	public const string ChaveEnergia = "energia", ChaveNivel = "nivel", ChaveEficiencia = "eficiencia", ChaveSolar = "solar";

	public const double EnergiaDeFabrica = 100, EficienciaDeFabrica = 0.5, EficienciaMaxima = 4, PassoDeEficiencia = 0.5;
	public const int NivelMaximo = 6;

	/// <summary>`elec_energy -= 5 / efficiency`: o que cada recarga (a do folego e a do Ki) tira da estacao.</summary>
	public static double CustoPorRecarga(double eficiencia) => 5 / Math.Max(eficiencia, 0.1);

	/// <summary>`M.Ki += 1 * efficiency * M.MaxKi / 1000`.</summary>
	public static double KiPorVolta(double eficiencia, double maxKi) => eficiencia * maxKi / 1000;

	/// <summary>`elec_energy_max = 100 * tier**2` do nivel 2 em diante; 100 no primeiro.</summary>
	public static double Capacidade(int nivel) => nivel >= 2 ? 100.0 * nivel * nivel : EnergiaDeFabrica;

	/// <summary>O que a estacao rende sozinha por volta: `tier` com paineis, e mais `tier` do nivel 2 em diante.</summary>
	public static double RendaPorVolta(int nivel, bool solar) => (solar ? nivel : 0) + (nivel >= 2 ? nivel : 0);

	public static double CustoDaEficiencia(double eficiencia) => 20_000 * eficiencia;
	public static double CustoDoNivel(int nivel) => 10_000.0 * nivel;
	public const double CustoDosPaineis = 100_000;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X3): a estacao recarrega qualquer raca -- e ela e dos androides.</summary>
	public static bool RecarregaQualquerUmDeTeste;
}

/// <summary>
/// A LUA ARTIFICIAL E O EMISSOR DE ONDAS BLUTZ -- `obj/items/Artificial_Moon` e `Black_Moon`
/// (`Code/Modules/Tech/Tier 1.dm:364-417`) e `obj/items/Emitter` (`Tier3.dm:69-97`).
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// A LUA, acesa (`Moon()`), olha a cada meio segundo quem a VE: o Saiyajin (ou meio-Saiyajin) de rabo vira Oozaru
/// (`M.Apeshit()`), e a lua se apaga e some dez segundos depois (`spawn(100) del(src)`). A LUA NEGRA (a lua
/// melhorada: um milhao de zeni e 50 de tecnologia) manda direto pro Oozaru DOURADO o Saiyajin que ja tem o Super
/// Saiyajin. O EMISSOR, por trinta segundos, faz o rabo do Saiyajin CRESCER DE VOLTA (`M.Tail_Grow()`) e manda
/// quem tem o Super Saiyajin pro Dourado.
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * ELA FICA ACESA ENQUANTO A FERA DURA, e nao dez segundos. Neste port a fera se desfaz no instante em que a
///     fonte dela some (regra do dono, 2026-09-06: *"a forma do oozaru e desfeita no momento que a fonte que deu o
///     poder pra virar (lua etc) sumir"*): uma lua de dez segundos faria um macaco de dez segundos. A lua acesa
///     dura o que o Oozaru dura (`Oozaru.Duracao`), e depois se apaga e some -- e quem a DESTROI antes disso
///     desfaz as feras que ela fez, que e a jogada classica.
///   * QUAL FERA SAI e a regra deste port (`Oozaru.QualSai`, `Oozaru.PodeDourado`): a lua comum faz o que a lua
///     cheia faz; a negra e o emissor mandam pro Dourado quem ja pode ser Dourado, e pra fera comum quem nao pode.
///     O SSJ4 por limiar de poder do emissor (`Tier3.dm:91-95`) nao vem: aqui ele se ganha dominando o Dourado.
///   * A MELHORIA PRA LUA NEGRA CONFERE AS DUAS COISAS. La a recusa esta escrita `if(usr.zenni&lt;cost &amp;&amp;
///     usr.techskill>=50)`: quem nao tem a tecnologia passa direto.
///   * CADA CORPO E OLHADO UMA VEZ por lua acesa: quem ela nao transformou (sem rabo, sem a maestria) nao ouve a
///     mesma recusa duas vezes por segundo.
/// ==================================================================================
/// </summary>
public static class LuaArtificial
{
	public const string Lua = "Artificial_Moon", Emissor = "Emitter";

	public const string VerboAcender = "lua_acender", VerboMelhorar = "lua_negra";

	// ---- as chaves do estado (`Obra.Tech.N`) ----
	public const string ChaveAcesaAte = "acesaAte", ChaveNegra = "negra";

	/// <summary>O estado do desenho da lua acesa (`icon_state="On"`) e o da apagada (`"Off"`).</summary>
	public const string ArteAcesa = "On", ArteApagada = "Off";

	/// <summary>`sleep(5)`: de quanto em quanto a lua acesa olha quem a ve.</summary>
	public const double SegundosEntreOlhares = 0.5;

	/// <summary>Ate onde ela alcanca, em tiles: o `view(src)` de la e a tela de quem olha.</summary>
	public const int RaioEmTiles = 9;

	/// <summary>"Upgrade_Moon": um milhao de zeni, e 50 de tecnologia.</summary>
	public const double CustoDaLuaNegra = 1_000_000, TecnologiaDaLuaNegra = 50;

	public static bool EhFonte(string tipo) => tipo is Lua or Emissor;

	/// <summary>Quanto tempo esta fonte fica acesa: o que dura a fera que ela faz (ver o cabecalho).</summary>
	public static double SegundosAcesa(string tipo, bool negra) =>
		Oozaru.Duracao(tipo == Emissor || negra ? FormaOozaru.Dourado : FormaOozaru.Regular);

	/// <summary>Esta fonte manda pro Dourado quem ja pode ser Dourado? (A lua negra e o emissor.)</summary>
	public static bool PuxaODourado(string tipo, bool negra) => tipo == Emissor || negra;

	/// <summary>Esta fonte faz o rabo crescer de volta? (So o emissor -- `M.Tail_Grow()`.)</summary>
	public static bool DevolveORabo(string tipo) => tipo == Emissor;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X3): a lua acesa nao e fonte de fera nenhuma -- o enfeite da v0.2.0.</summary>
	public static bool NaoEFonteDeTeste;
}
