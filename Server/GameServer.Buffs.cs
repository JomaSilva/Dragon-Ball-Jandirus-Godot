using System.Reflection;
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
/// desliga desfazendo. Sao sete so no primeiro lote (Brutal Clarity, Extreme Burst, Fighting
/// Power, Ultradense Body, Ki Blade, Ki Sword, Super Majin). Sem um lugar comum, seriam sete
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
		return true;
	}

	/// <summary>DESLIGA, desfazendo exatamente o que foi aplicado. False se nao estava ligado.</summary>
	private bool DesligarBuff(ServerPlayer pl, string id)
	{
		Dictionary<string, BuffAtivo> meus = BuffsDe(pl.Id);
		if (!meus.Remove(id, out BuffAtivo? b)) return false;

		foreach ((string campo, double v) in b.Somas) Somar(pl.Ficha, campo, -v);
		foreach ((string campo, double v) in b.Fatores) Multiplicar(pl.Ficha, campo, 1 / v);
		if (b.Dreno != 0 && b.Dreno != 1) pl.Ficha.DrainMod /= b.Dreno;

		SincronizarBuffDeKi(pl);   // ver `BuffsDeKi`: a chave e DERIVADA do que sobrou ligado
		pl.Ficha.Statify();
		pl.SigAtributos = "";
		MandarEfeito(pl, id, 0);
		return true;
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
	/// DERRUBA TUDO. Chamado no nocaute, na morte e ao sair -- e a rede de seguranca que impede
	/// buff de sobreviver ao corpo que o sustentava.
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

			if (pl.Ficha.KO || pl.Ficha.dead) { DerrubarBuffs(pl); continue; }

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
