using Godot;

namespace Jandirus.Server;

/// <summary>
/// O BANCO -- guardar zeni onde morrer não alcança.
///
/// ============================ DE ONDE VEM ============================
/// É o `obj/Bank` do original (`Modules/Tech/Bank.dm:25-40`), com os verbos `Bank`,
/// `Deposit_Item` e `Retrieve_Item`. Ele está espalhado pelos `.dmm` desde sempre -- dezessete
/// deles, um por cidade -- e no port era uma célula de tilemap: desenho parado, sem resposta.
///
/// Agora ele é uma CONSTRUÇÃO como qualquer outra (ver `GameServer.CarregarObjetosDoMapa`), o que
/// significa que ele já bloqueia, já é desenhado pelo mesmo pacote e já entra no alcance de uso.
/// O que faltava era o que ele FAZ, e é só isto aqui.
/// =====================================================================
///
/// SÓ O ZENI. O `Deposit_Item`/`Retrieve_Item` do original mexe em itens e módulos, e o port não
/// tem sistema de itens -- fingir um cofre vazio seria pior que não ter cofre. Quando os itens
/// existirem, eles entram por aqui.
///
/// POR QUE UM BANCO IMPORTA: morrer custa zeni, e o que está guardado não se perde. Ele é a
/// única poupança do jogo, e é o que faz uma construção de meio milhão ser alcançável por quem
/// apanha no caminho.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// TRATA OS COMANDOS DO BANCO. Devolve falso quando não é um deles -- mesmo contrato do
	/// despacho de admin.
	/// </summary>
	private bool ComandoDeBanco(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "banco_ver": BancoExtrato(pl); return true;
			case "banco_depositar": BancoMover(pl, arg, depositar: true); return true;
			case "banco_sacar": BancoMover(pl, arg, depositar: false); return true;
			default: return false;
		}
	}

	/// <summary>
	/// O CAIXA MAIS PRÓXIMO, ou nulo. Vale pra qualquer banco -- eles não têm dono.
	///
	/// A CONFERÊNCIA É DO SERVIDOR, e é por isso que ela existe aqui e não só no botão: o cliente
	/// esconde o painel quando não há banco por perto, e esconder botão nunca foi permissão.
	/// </summary>
	private Obra? CaixaPerto(ServerPlayer pl)
	{
		Obra? o = ObraPerto(pl);
		return o is { Tipo: "Bank" } ? o : null;
	}

	private void BancoExtrato(ServerPlayer pl)
	{
		if (CaixaPerto(pl) == null) { Avisar(pl, "voce precisa estar num banco."); return; }
		Avisar(pl, $"--- banco --- no bolso: {pl.Ficha.Zeni:N0} zeni | guardado: {pl.Ficha.ZeniBanco:N0} zeni");
	}

	/// <summary>
	/// MOVE ZENI entre o bolso e o cofre.
	///
	/// O ARGUMENTO VAZIO QUER DIZER TUDO. É o atalho que se usa noventa por cento das vezes
	/// (chegar, guardar tudo, sair), e obrigar a digitar o saldo exato pra isso seria pedir que o
	/// jogador leia um número pra repetir o mesmo número.
	/// </summary>
	private void BancoMover(ServerPlayer pl, string arg, bool depositar)
	{
		if (CaixaPerto(pl) == null) { Avisar(pl, "voce precisa estar num banco."); return; }

		double tenho = depositar ? pl.Ficha.Zeni : pl.Ficha.ZeniBanco;
		if (tenho < 1)
		{
			Avisar(pl, depositar ? "voce nao tem zeni no bolso." : "voce nao tem nada guardado.");
			return;
		}

		double quanto = arg.Trim().Length == 0 ? tenho
			: double.TryParse(arg.Trim(), System.Globalization.NumberStyles.Float,
							  System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : -1;

		if (quanto <= 0) { Avisar(pl, "diga quanto -- ou deixe em branco pra mover tudo."); return; }

		// SEM ARREDONDAR PRA CIMA: pedir mais do que se tem move o que se tem, e não recusa. É o
		// que qualquer caixa faz, e evita o vaivém de "quanto mesmo eu tinha?".
		quanto = Math.Floor(Math.Min(quanto, tenho));
		if (quanto < 1) { Avisar(pl, "e pouco demais pra mover."); return; }

		if (depositar) { pl.Ficha.Zeni -= quanto; pl.Ficha.ZeniBanco += quanto; }
		else { pl.Ficha.ZeniBanco -= quanto; pl.Ficha.Zeni += quanto; }

		Persistir(pl);
		// A ABA TECH MOSTRA O ZENI: sem reenviar, o número na tela fica velho até a próxima vez
		// que alguém construir alguma coisa.
		MandarCatalogoDeObras(pl);

		Avisar(pl, depositar
			? $"voce guarda {quanto:N0} zeni. No cofre: {pl.Ficha.ZeniBanco:N0}. No bolso: {pl.Ficha.Zeni:N0}."
			: $"voce saca {quanto:N0} zeni. No bolso: {pl.Ficha.Zeni:N0}. No cofre: {pl.Ficha.ZeniBanco:N0}.");
		GD.Print($"[banco] {pl.Name} {(depositar ? "depositou" : "sacou")} {quanto:N0} zeni");
	}
}
