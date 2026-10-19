using System.Reflection;
using System.Text.Json;
using Godot;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>Um buff LIGADO agora: o que ele somou, e o que ele multiplicou.</summary>
public sealed class BuffAtivo
{
	public string Id = "";
	public string Nome = "";

	/// <summary>Campo do lutador -> quanto foi SOMADO. Guardado pra desfazer exatamente isto.</summary>
	public Dictionary<string, double> Somas = new(StringComparer.Ordinal);

	/// <summary>Campo do lutador -> por quanto foi MULTIPLICADO. Desfaz dividindo, nunca subtraindo.</summary>
	public Dictionary<string, double> Fatores = new(StringComparer.Ordinal);

	/// <summary>Fator aplicado em `DrainMod` -- o preco de manter o buff de pe.</summary>
	public double Dreno = 1;

	/// <summary>Quando acaba sozinho, em ms. Zero = so sai desligando.</summary>
	public long ExpiraEm;
}

/// <summary>
/// BUFFS TEMPORARIOS -- o `startbuff` / `stopbuff` / `isBuffed` do original.
///
/// POR QUE ISTO EXISTE EM VEZ DE CADA TECNICA FAZER O SEU: metade das tecnicas ativas do jogo tem
/// exatamente a mesma forma -- liga, soma alguma coisa num `T*`, multiplica o dreno de Ki, e
/// desliga desfazendo. Sao seis so no primeiro lote (Brutal Clarity, Extreme Burst, Fighting
/// Power, Ultradense Body, Ki Blade, Ki Sword). Sem um lugar comum, seriam seis
/// copias da mesma logica de desfazer -- e basta UMA delas desfazer errado pra virar poder
/// permanente de graca.
///
/// A REGRA DE OURO E GUARDAR O QUE FOI APLICADO. Desfazer recalculando (`Tphysoff -= bonus()`)
/// parece equivalente e nao e: se o jogador ficar mais forte com o buff de pe, o bonus recalculado
/// no fim e MAIOR que o aplicado no comeco, e ele sai com o stat NEGATIVO. O original tem
/// exatamente esse bug em varios lugares; aqui o `BuffAtivo` carrega os numeros originais.
///
/// O DRENO E MULTIPLICATIVO E COMPOE. Dois buffs de 5x deixam o Ki indo embora 25 vezes mais
/// rapido -- e de proposito: e o que impede ligar tudo ao mesmo tempo e nunca desligar.
/// </summary>
public partial class GameServer
{
	private readonly Dictionary<int, Dictionary<string, BuffAtivo>> _buffs = [];

	private Dictionary<string, BuffAtivo> BuffsDe(int id)
	{
		if (!_buffs.TryGetValue(id, out Dictionary<string, BuffAtivo>? d)) _buffs[id] = d = [];
		return d;
	}

	public bool TemBuff(ServerPlayer pl, string id) => BuffsDe(pl.Id).ContainsKey(id);

	/// <summary>
	/// OS BUFFS QUE CONTAM COMO "BUFF DE KI" -- os tres `/obj/buff` que escrevem
	/// `container.kibuffon = 1` no original (`Ki2.0/KiBuffs.dm:28`, `:57`, `:98`).
	///
	/// ============================ ELES SAO A CHAVE DE UMA ARVORE INTEIRA ============================
	/// `kibuffon` nao e enfeite de HUD: e a fonte de exp das SEIS skills de Circulacao de Ki
	/// (Basic/Advanced/Perfect, `Mind.dm:243`, `:513`, `:726`), o portao do <see cref="Fighter.buffregen"/>
	/// (a cura passiva do nivel 30 da Advanced), e o que enche o `kibuffcounter` que treina a familia
	/// Buff Mastery (`Mind.dm:133`). Enquanto o campo nao existia, ligar o Foco nao treinava NADA.
	///
	/// A LISTA E DOS TRES E SO DOS TRES. Kaio-ken, Ki Blade e os sete buffs de corpo do lote G1
	/// tambem sao buffs, e no DM nenhum deles escreve `kibuffon` -- inclui-los daria exp de
	/// Circulacao a quem ligasse uma lamina, que e outra arvore.
	/// ============================================================================================
	/// </summary>
	private static readonly HashSet<string> BuffsDeKi =
		new(StringComparer.Ordinal) { "Focus", "Efficiency", "Energy_Shield" };

	/// <summary>
	/// REESCREVE `kibuffon` a partir do que esta ligado AGORA.
	///
	/// Derivado, e nao um contador que sobe e desce: ligar Foco e Escudo juntos e depois desligar um
	/// deles tem que deixar a chave LIGADA, e no DM isso e um defeito real (`DeBuff()` de qualquer um
	/// dos tres zera a chave dos outros dois, `KiBuffs.dm:32`). Perguntar ao conjunto acerta os dois
	/// casos sem guardar estado nenhum.
	/// </summary>
	private void SincronizarBuffDeKi(ServerPlayer pl)
	{
		Dictionary<string, BuffAtivo> meus = BuffsDe(pl.Id);
		bool algum = false;
		foreach (string id in BuffsDeKi)
			if (meus.ContainsKey(id)) { algum = true; break; }
		pl.Ficha.kibuffon = algum ? 1 : 0;
	}

	/// <summary>
	/// LIGA um buff. `somas` sao campos do <see cref="Fighter"/> (por reflexao, como os efeitos de
	/// skill); `dreno` multiplica o `DrainMod`. `duracaoMs` de 0 = fica ate desligarem.
	///
	/// Devolve false se ja estava ligado -- quem chama decide se isso e erro ou toggle.
	/// </summary>
	/// <param name="fatores">
	/// CAMPOS QUE O BUFF MULTIPLICA em vez de somar -- o `container.superkiarmorMod *= 1.2` do
	/// Energy Shield (`Ki2.0/KiBuffs.dm:88`) e o primeiro deles.
	///
	/// Entrou como canal proprio, e nao como "soma equivalente", porque os dois desfazem de jeitos
	/// DIFERENTES: soma se tira subtraindo, fator se tira dividindo, e um fator desfeito por
	/// subtracao deixa o campo num numero que nao e o de antes nem o de depois. E exatamente a
	/// separacao que o <see cref="Jandirus.Core.Skills.EfeitosDeSkill"/> ja tinha descoberto
	/// precisar nos efeitos passivos (canal 1 contra canal 2); aqui ela chega nos temporarios.
	/// </param>
	private bool LigarBuff(ServerPlayer pl, string id, string nome,
						   Dictionary<string, double> somas, double dreno = 1, long duracaoMs = 0,
						   Dictionary<string, double>? fatores = null)
	{
		Dictionary<string, BuffAtivo> meus = BuffsDe(pl.Id);
		if (meus.ContainsKey(id)) return false;

		var b = new BuffAtivo { Id = id, Nome = nome, Dreno = dreno };
		foreach ((string campo, double v) in somas)
		{
			if (!Somar(pl.Ficha, campo, v)) continue;   // campo que o port nao tem: nao finge
			b.Somas[campo] = v;
		}
		if (fatores != null)
			foreach ((string campo, double v) in fatores)
			{
				if (v == 0 || !Multiplicar(pl.Ficha, campo, v)) continue;
				b.Fatores[campo] = v;
			}
		if (dreno != 1) pl.Ficha.DrainMod *= dreno;
		if (duracaoMs > 0) b.ExpiraEm = NowMs() + duracaoMs;

		meus[id] = b;
		SincronizarBuffDeKi(pl);   // `container.kibuffon = 1` dos tres buffs de Ki -- ver `BuffsDeKi`
		pl.Ficha.Statify();
		pl.SigAtributos = "";
		MandarEfeito(pl, id, duracaoMs > 0 ? duracaoMs : -1);
		SincronizarPorte(pl, id);
		return true;
	}

	/// <summary>DESLIGA, desfazendo exatamente o que foi aplicado. False se nao estava ligado.</summary>
	private bool DesligarBuff(ServerPlayer pl, string id)
	{
		Dictionary<string, BuffAtivo> meus = BuffsDe(pl.Id);
		if (!meus.Remove(id, out BuffAtivo? b)) return false;

		Desfazer(pl.Ficha, b);

		SincronizarBuffDeKi(pl);   // ver `BuffsDeKi`: a chave e DERIVADA do que sobrou ligado
		pl.Ficha.Statify();
		pl.SigAtributos = "";
		MandarEfeito(pl, id, 0);
		SincronizarPorte(pl, id);
		return true;
	}

	/// <summary>DESFAZ NUMA FICHA exatamente o que um buff aplicou: a soma subtraindo, o fator e o dreno dividindo.</summary>
	private static void Desfazer(Fighter f, BuffAtivo b)
	{
		foreach ((string campo, double v) in b.Somas) Somar(f, campo, -v);
		foreach ((string campo, double v) in b.Fatores) Multiplicar(f, campo, 1 / v);
		if (b.Dreno != 0 && b.Dreno != 1) f.DrainMod /= b.Dreno;
	}

	/// <summary>
	/// (v0.3.1) A FICHA COMO ELA FICA SEM OS BUFFS DE AGORA -- e e ESTA que vai pro disco (`Persistir`).
	///
	/// ============================ O KAIO-KEN QUE FICAVA PRESO (dono, 2026-10-16) ============================
	/// *"meu amigo ... agora ele n consegue mais escolher o kaioken, e fica travado no kaioken times 6"*. O save grava o
	/// `Fighter` INTEIRO (`AccountStore.DeJogador`), e o buff mora nele: o `KaioPcnt` do Kaio-ken, os `T*` dos buffs de corpo,
	/// o `DrainMod`. O registro do buff (`_buffs`) e da SESSAO e nao vai pro disco. Quem saia do jogo com um buff aceso (o
	/// `Drop` grava sem derrubar nada, e o salvamento de dois em dois minutos grava com tudo de pe) voltava com o NUMERO do buff
	/// na ficha e sem o buff: o Kaio-ken "aceso" pra sempre (a ficha lenta diz aceso pelo `KaioPcnt`), cada aperto do botao
	/// acendendo outro x6 POR CIMA, e a tela de escolha nunca mais abrindo. No DM o logout derruba os buffs ANTES do save
	/// (`clearbuffs()` no `DoLogoutStuff`, `Login.dm:204`).
	///
	/// AQUI, NO SAVE, E NAO SO NO LOGOUT: o salvamento periodico e os outros `Persistir` do jogo gravam no meio do buff, e um
	/// servidor que caia ou feche entre um deles e o logout deixaria o mesmo numero preso. A copia vai pelo JSON (o caminho do
	/// proprio save, como a do `DarCorpoProprio`), e a ficha VIVA nao e tocada: o buff segue aceso, e apagar continua devolvendo
	/// cada numero exato.
	/// =========================================================================================================
	/// </summary>
	private Fighter FichaSemOsBuffs(ServerPlayer pl)
	{
		Dictionary<string, BuffAtivo> meus = BuffsDe(pl.Id);
		if (meus.Count == 0 || BuffVaiProDiscoDeTeste) return pl.Ficha;
		Fighter copia = JsonSerializer.Deserialize<Fighter>(JsonSerializer.Serialize(pl.Ficha, CopiaDeFicha), CopiaDeFicha)!;
		foreach (BuffAtivo b in meus.Values) Desfazer(copia, b);
		copia.kibuffon = 0;   // a chave e DERIVADA dos buffs de pe (`SincronizarBuffDeKi`): sem nenhum, desligada
		copia.Statify();
		return copia;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v030teste`, familia KP): o save volta a levar os buffs acesos na ficha -- o jogo de antes.
	/// Lido pela <see cref="FichaSemOsBuffs"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool BuffVaiProDiscoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v030teste`, familia KP): o login volta a aceitar o Kaio-ken gravado aceso -- quem ficou
	/// preso continua preso. Lido pelo `AccountStore.ParaJogador`. Falso em jogo, sempre.
	/// </summary>
	internal static bool KaiokenAcesoAtravessaOLoginDeTeste;

	/// <summary>
	/// (2026-10-15) O TAMANHO DO CORPO E DE QUEM VE: a Forma Gigante muda o porte do corpo, e a zona inteira precisa
	/// redesenha-lo -- o bit vai nas marcas do `S2C.PeerLook` (`PacoteDeAparencia`). Por aqui passam a entrada e as tres
	/// saidas da forma (o verb de novo, o folego que acaba, a queda que derruba os buffs) sem nenhuma delas lembrar; e vale
	/// pro NPC tambem (o Lorde Slug cresce no meio da luta), por isso nao e o `ReapresentarOCorpo`.
	/// </summary>
	private void SincronizarPorte(ServerPlayer pl, string id)
	{
		if (id == BuffGiganteG2) ReapresentarAparencia(pl);
	}

	/// <summary>Liga se estava desligado, desliga se estava ligado. Devolve o estado NOVO.</summary>
	private bool AlternarBuff(ServerPlayer pl, string id, string nome,
							  Dictionary<string, double> somas, double dreno = 1)
	{
		if (DesligarBuff(pl, id)) return false;
		LigarBuff(pl, id, nome, somas, dreno);
		return true;
	}

	/// <summary>
	/// DERRUBA TUDO. Chamado no nocaute e na morte -- e a rede de seguranca que impede
	/// buff de sobreviver ao corpo que o sustentava. (A SAIDA do jogo nao passa por aqui: quem impede o buff de ir pro disco e
	/// o `Persistir`, que grava a ficha sem ele -- ver <see cref="FichaSemOsBuffs"/>.)
	///
	/// SEM ISTO O BUFF VIRA PATRIMONIO: um jogador que apaga com Kaioken ligado acordaria com o
	/// multiplicador de pe e sem nada consumindo Ki. E o jeito classico de um numero temporario
	/// virar permanente.
	/// </summary>
	private void DerrubarBuffs(ServerPlayer pl)
	{
		foreach (string id in BuffsDe(pl.Id).Keys.ToList()) DesligarBuff(pl, id);
	}

	/// <summary>
	/// COBRA E EXPIRA. Roda uma vez por segundo junto do resto.
	///
	/// O DRENO NAO E COBRADO AQUI: ele ja vive no `DrainMod`, que multiplica o custo de tudo e o
	/// consumo passivo. Cobrar de novo aqui seria cobrar duas vezes -- e o tipo de engano que so
	/// aparece como "esse buff drena rapido demais" numa reclamacao de balanceamento.
	/// </summary>
	private void TickDosBuffs()
	{
		long agora = NowMs();
		foreach ((int id, Dictionary<string, BuffAtivo> meus) in _buffs)
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) continue;

			// (2026-10-15) O CADAVER perde os buffs; o fantasma de pe do Outro Mundo os mantem -- o Kaio-ken do Sr. Kaioh e de morto.
			if (pl.Ficha.KO || EhCadaver(pl)) { DerrubarBuffs(pl); continue; }

			foreach (BuffAtivo b in meus.Values.ToList())
			{
				if (b.ExpiraEm > 0 && agora >= b.ExpiraEm)
				{
					DesligarBuff(pl, b.Id);
					Avisar(pl, $"{b.Nome} se desfaz.");
					continue;
				}
				// SEM KI NAO HA COMO SUSTENTAR. O original derruba os buffs caros quando o Ki
				// zera; sem isso da pra andar pra sempre com o multiplicador ligado e Ki 0.
				if (b.Dreno > 1 && pl.Ficha.Ki <= 0)
				{
					DesligarBuff(pl, b.Id);
					Avisar(pl, $"voce nao consegue mais sustentar {b.Nome}.");
				}
			}
		}
	}

	/// <summary>Soma num campo do lutador pelo nome. False se o port nao tem o campo.</summary>
	private static bool Somar(Fighter f, string campo, double delta)
	{
		FieldInfo? fi = typeof(Fighter).GetField(campo, BindingFlags.Public | BindingFlags.Instance);
		if (fi == null || fi.FieldType != typeof(double))
		{
			GD.PushWarning($"[buff] campo desconhecido no Fighter: {campo}");
			return false;
		}
		fi.SetValue(f, (double)fi.GetValue(f)! + delta);
		return true;
	}

	/// <summary>Multiplica um campo do lutador pelo nome. False se o port nao tem o campo.</summary>
	private static bool Multiplicar(Fighter f, string campo, double razao)
	{
		FieldInfo? fi = typeof(Fighter).GetField(campo, BindingFlags.Public | BindingFlags.Instance);
		if (fi == null || fi.FieldType != typeof(double))
		{
			GD.PushWarning($"[buff] campo desconhecido no Fighter: {campo}");
			return false;
		}
		fi.SetValue(f, (double)fi.GetValue(f)! * razao);
		return true;
	}
}
