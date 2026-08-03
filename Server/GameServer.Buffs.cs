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
	/// LIGA um buff. `somas` sao campos do <see cref="Fighter"/> (por reflexao, como os efeitos de
	/// skill); `dreno` multiplica o `DrainMod`. `duracaoMs` de 0 = fica ate desligarem.
	///
	/// Devolve false se ja estava ligado -- quem chama decide se isso e erro ou toggle.
	/// </summary>
	private bool LigarBuff(ServerPlayer pl, string id, string nome,
						   Dictionary<string, double> somas, double dreno = 1, long duracaoMs = 0)
	{
		Dictionary<string, BuffAtivo> meus = BuffsDe(pl.Id);
		if (meus.ContainsKey(id)) return false;

		var b = new BuffAtivo { Id = id, Nome = nome, Dreno = dreno };
		foreach ((string campo, double v) in somas)
		{
			if (!Somar(pl.Ficha, campo, v)) continue;   // campo que o port nao tem: nao finge
			b.Somas[campo] = v;
		}
		if (dreno != 1) pl.Ficha.DrainMod *= dreno;
		if (duracaoMs > 0) b.ExpiraEm = NowMs() + duracaoMs;

		meus[id] = b;
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
		if (b.Dreno != 0 && b.Dreno != 1) pl.Ficha.DrainMod /= b.Dreno;

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
}
